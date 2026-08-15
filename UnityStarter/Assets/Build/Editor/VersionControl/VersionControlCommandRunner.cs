using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Build.VersionControl.Editor
{
    internal interface IVersionControlCommandRunner
    {
        string Run(
            string executable,
            string arguments,
            string workingDirectory,
            IReadOnlyDictionary<string, string> environment,
            int timeoutMilliseconds,
            int maximumOutputCharacters,
            bool allowExitCodeOne = false);
    }

    internal interface ICancellableVersionControlCommandRunner
    {
        string Run(
            string executable,
            string arguments,
            string workingDirectory,
            IReadOnlyDictionary<string, string> environment,
            int timeoutMilliseconds,
            int maximumOutputCharacters,
            bool allowExitCodeOne,
            CancellationToken cancellationToken);
    }

    internal sealed class VersionControlCommandException : InvalidOperationException
    {
        internal VersionControlCommandException(string failureCode, string message)
            : base(message)
        {
            FailureCode = failureCode ?? VersionControlWorkspaceEvidence.CommandFailed;
        }

        public string FailureCode { get; }
    }

    internal sealed class VersionControlCommandRunner :
        IVersionControlCommandRunner,
        ICancellableVersionControlCommandRunner
    {
        private static readonly MethodInfo KillProcessTreeMethod = typeof(Process).GetMethod(
            "Kill",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: new[] { typeof(bool) },
            modifiers: null);

        public string Run(
            string executable,
            string arguments,
            string workingDirectory,
            IReadOnlyDictionary<string, string> environment,
            int timeoutMilliseconds,
            int maximumOutputCharacters,
            bool allowExitCodeOne = false)
        {
            return Run(
                executable,
                arguments,
                workingDirectory,
                environment,
                timeoutMilliseconds,
                maximumOutputCharacters,
                allowExitCodeOne,
                CancellationToken.None);
        }

        public string Run(
            string executable,
            string arguments,
            string workingDirectory,
            IReadOnlyDictionary<string, string> environment,
            int timeoutMilliseconds,
            int maximumOutputCharacters,
            bool allowExitCodeOne,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(executable))
            {
                throw new ArgumentException("Command executable is required.", nameof(executable));
            }

            if (timeoutMilliseconds <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
            }

            if (maximumOutputCharacters <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumOutputCharacters));
            }

            cancellationToken.ThrowIfCancellationRequested();

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = arguments ?? string.Empty,
                WorkingDirectory = Path.GetFullPath(
                    workingDirectory ?? throw new ArgumentNullException(nameof(workingDirectory))),
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            if (environment != null)
            {
                foreach (KeyValuePair<string, string> entry in environment)
                {
                    startInfo.EnvironmentVariables[entry.Key] = entry.Value ?? string.Empty;
                }
            }

            using (var process = new Process { StartInfo = startInfo })
            {
                var stopwatch = Stopwatch.StartNew();
                try
                {
                    if (!process.Start())
                    {
                        throw CreateFailure(
                            VersionControlWorkspaceEvidence.ExecutableUnavailable,
                            "Version-control command could not be started.");
                    }
                }
                catch (Win32Exception exception)
                {
                    throw new VersionControlCommandException(
                        VersionControlWorkspaceEvidence.ExecutableUnavailable,
                        "Version-control executable is unavailable: " + exception.GetType().Name);
                }

                var budget = new SharedOutputBudget(maximumOutputCharacters);
                Task<string> outputTask = Task.Run(
                    () => ReadBounded(process.StandardOutput, budget, process));
                Task<string> errorTask = Task.Run(
                    () => ReadBounded(process.StandardError, budget, process));

                int processWaitMilliseconds = RemainingMilliseconds(
                    stopwatch,
                    timeoutMilliseconds);
                if (!WaitForExit(process, processWaitMilliseconds, cancellationToken))
                {
                    TryKill(process);
                    TryCloseReaders(process);
                    ObserveEventually(outputTask);
                    ObserveEventually(errorTask);
                    cancellationToken.ThrowIfCancellationRequested();
                    throw CreateFailure(
                        VersionControlWorkspaceEvidence.CommandTimedOut,
                        "Version-control command exceeded its time budget.");
                }

                int readerWaitMilliseconds = RemainingMilliseconds(
                    stopwatch,
                    timeoutMilliseconds);
                if (!WaitForReaders(
                        outputTask,
                        errorTask,
                        readerWaitMilliseconds,
                        cancellationToken))
                {
                    TryKill(process);
                    TryCloseReaders(process);
                    ObserveEventually(outputTask);
                    ObserveEventually(errorTask);
                    cancellationToken.ThrowIfCancellationRequested();
                    throw CreateFailure(
                        VersionControlWorkspaceEvidence.CommandTimedOut,
                        "Version-control command output did not close within its time budget.");
                }

                if (budget.Exceeded)
                {
                    ObserveCompleted(outputTask);
                    ObserveCompleted(errorTask);
                    throw CreateFailure(
                        VersionControlWorkspaceEvidence.OutputLimitExceeded,
                        "Version-control command exceeded its output budget.");
                }

                string output = GetReaderResult(outputTask);
                string error = GetReaderResult(errorTask);
                if (process.ExitCode != 0
                    && !(allowExitCodeOne && process.ExitCode == 1))
                {
                    throw CreateFailure(
                        VersionControlWorkspaceEvidence.CommandFailed,
                        $"Version-control command failed with exit code {process.ExitCode}.");
                }

                if (process.ExitCode == 1
                    && allowExitCodeOne
                    && !string.IsNullOrWhiteSpace(error))
                {
                    throw CreateFailure(
                        VersionControlWorkspaceEvidence.CommandFailed,
                        "Version-control command failed with diagnostic output.");
                }

                return output;
            }
        }

        private static string ReadBounded(
            TextReader reader,
            SharedOutputBudget budget,
            Process process)
        {
            var builder = new StringBuilder(Math.Min(4096, budget.MaximumCharacters));
            var buffer = new char[2048];
            while (true)
            {
                int read = reader.Read(buffer, 0, buffer.Length);
                if (read <= 0)
                {
                    return builder.ToString();
                }

                int accepted = budget.Claim(read);
                if (accepted > 0)
                {
                    builder.Append(buffer, 0, accepted);
                }

                if (accepted != read)
                {
                    TryKill(process);
                    return builder.ToString();
                }
            }
        }

        internal static bool WaitForReaders(
            Task outputTask,
            Task errorTask,
            int timeoutMilliseconds)
        {
            return WaitForReaders(
                outputTask,
                errorTask,
                timeoutMilliseconds,
                CancellationToken.None);
        }

        internal static bool WaitForReaders(
            Task outputTask,
            Task errorTask,
            int timeoutMilliseconds,
            CancellationToken cancellationToken)
        {
            if (outputTask == null)
            {
                throw new ArgumentNullException(nameof(outputTask));
            }

            if (errorTask == null)
            {
                throw new ArgumentNullException(nameof(errorTask));
            }

            if (timeoutMilliseconds <= 0)
            {
                return outputTask.IsCompleted && errorTask.IsCompleted;
            }

            return SpinWait.SpinUntil(
                () => cancellationToken.IsCancellationRequested
                      || (outputTask.IsCompleted && errorTask.IsCompleted),
                timeoutMilliseconds)
                   && !cancellationToken.IsCancellationRequested;
        }

        private static bool WaitForExit(
            Process process,
            int timeoutMilliseconds,
            CancellationToken cancellationToken)
        {
            if (timeoutMilliseconds <= 0)
            {
                return false;
            }

            var stopwatch = Stopwatch.StartNew();
            while (!cancellationToken.IsCancellationRequested)
            {
                int remaining = timeoutMilliseconds - (int)Math.Min(
                    timeoutMilliseconds,
                    stopwatch.ElapsedMilliseconds);
                if (remaining <= 0)
                {
                    return process.HasExited;
                }

                if (process.WaitForExit(Math.Min(remaining, 100)))
                {
                    return true;
                }
            }

            return false;
        }

        private static int RemainingMilliseconds(
            Stopwatch stopwatch,
            int timeoutMilliseconds)
        {
            long remaining = timeoutMilliseconds - stopwatch.ElapsedMilliseconds;
            return remaining <= 0
                ? 0
                : remaining >= int.MaxValue
                    ? int.MaxValue
                    : (int)remaining;
        }

        private static string GetReaderResult(Task<string> task)
        {
            try
            {
                return task.GetAwaiter().GetResult();
            }
            catch (ObjectDisposedException)
            {
                throw CreateFailure(
                    VersionControlWorkspaceEvidence.CommandFailed,
                    "Version-control command output stream closed unexpectedly.");
            }
            catch (IOException)
            {
                throw CreateFailure(
                    VersionControlWorkspaceEvidence.CommandFailed,
                    "Version-control command output stream failed.");
            }
        }

        private static void ObserveCompleted(Task task)
        {
            try
            {
                task.GetAwaiter().GetResult();
            }
            catch (Exception)
            {
            }
        }

        private static void ObserveEventually(Task task)
        {
            task.ContinueWith(
                completed =>
                {
                    if (completed.IsFaulted)
                    {
                        _ = completed.Exception;
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private static VersionControlCommandException CreateFailure(
            string failureCode,
            string message)
        {
            return new VersionControlCommandException(failureCode, message);
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    if (KillProcessTreeMethod != null)
                    {
                        KillProcessTreeMethod.Invoke(process, new object[] { true });
                    }
                    else
                    {
                        process.Kill();
                    }
                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch (InvalidOperationException)
            {
            }
            catch (Win32Exception)
            {
            }
            catch (TargetInvocationException)
            {
                TryKillDirect(process);
            }
        }

        private static void TryKillDirect(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch (InvalidOperationException)
            {
            }
            catch (Win32Exception)
            {
            }
        }

        private static void TryCloseReaders(Process process)
        {
            try
            {
                process.StandardOutput.Dispose();
            }
            catch (Exception)
            {
            }

            try
            {
                process.StandardError.Dispose();
            }
            catch (Exception)
            {
            }
        }

        private sealed class SharedOutputBudget
        {
            private int claimedCharacters;
            private int exceeded;

            internal SharedOutputBudget(int maximumCharacters)
            {
                MaximumCharacters = maximumCharacters;
            }

            internal int MaximumCharacters { get; }
            internal bool Exceeded => Volatile.Read(ref exceeded) != 0;

            internal int Claim(int requested)
            {
                int after = Interlocked.Add(ref claimedCharacters, requested);
                if (after <= MaximumCharacters)
                {
                    return requested;
                }

                Interlocked.Exchange(ref exceeded, 1);
                int before = after - requested;
                return Math.Max(0, MaximumCharacters - before);
            }
        }
    }
}
