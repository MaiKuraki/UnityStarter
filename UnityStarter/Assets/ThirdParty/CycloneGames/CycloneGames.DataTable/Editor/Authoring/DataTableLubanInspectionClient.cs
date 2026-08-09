using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace CycloneGames.DataTable.Unity.Editor
{
    internal enum DataTableLubanInspectionProcessStartOutcome
    {
        Started,
        FailedToStart,
        RejectedForShutdown,
    }

    internal delegate bool DataTableLubanInspectionProcessStarter(Process process);

    internal delegate bool DataTableLubanInspectionProcessTerminator(
        Process process,
        int timeoutMilliseconds,
        out string error);

    internal delegate void DataTableLubanInspectionProcessDisposer(Process process);

    internal static class DataTableLubanInspectionOwnership
    {
        internal static bool CanRelease(
            bool cancelledOrTimedOut,
            bool readersCompleted,
            bool treeTerminationConfirmed)
        {
            return treeTerminationConfirmed || (!cancelledOrTimedOut && readersCompleted);
        }
    }

    /// <summary>
    /// Owns the lifecycle gate for inspection child processes. Starting and registering are
    /// atomic with respect to shutdown so no child can cross an Editor reload boundary unseen.
    /// </summary>
    internal sealed class DataTableLubanInspectionProcessRegistry
    {
        private readonly object _sync = new object();
        private readonly List<OwnedProcess> _activeProcesses = new List<OwnedProcess>(2);
        private readonly DataTableLubanInspectionProcessStarter _starter;
        private readonly DataTableLubanInspectionProcessTerminator _terminator;
        private readonly DataTableLubanInspectionProcessDisposer _disposer;
        private bool _shutdownRequested;

        internal DataTableLubanInspectionProcessRegistry(
            DataTableLubanInspectionProcessStarter starter,
            DataTableLubanInspectionProcessTerminator terminator,
            DataTableLubanInspectionProcessDisposer disposer = null)
        {
            _starter = starter ?? throw new ArgumentNullException(nameof(starter));
            _terminator = terminator ?? throw new ArgumentNullException(nameof(terminator));
            _disposer = disposer ?? DisposeProcess;
        }

        internal DataTableLubanInspectionProcessStartOutcome TryStartAndRegister(Process process)
        {
            if (process == null)
            {
                throw new ArgumentNullException(nameof(process));
            }

            lock (_sync)
            {
                if (_shutdownRequested)
                {
                    return DataTableLubanInspectionProcessStartOutcome.RejectedForShutdown;
                }

                if (!_starter(process))
                {
                    return DataTableLubanInspectionProcessStartOutcome.FailedToStart;
                }

                _activeProcesses.Add(new OwnedProcess(process));
                return DataTableLubanInspectionProcessStartOutcome.Started;
            }
        }

        internal bool ReleaseIfConfirmed(Process process, bool confirmed)
        {
            if (!confirmed)
            {
                return false;
            }

            OwnedProcess owned;
            lock (_sync)
            {
                owned = FindOwnedProcessLocked(process);
            }

            return owned == null || ReleaseOwnedProcess(owned, out _);
        }

        internal bool TryTerminateOwnedProcess(
            Process process,
            int timeoutMilliseconds,
            out string error)
        {
            if (process == null)
            {
                error = string.Empty;
                return true;
            }

            OwnedProcess owned;
            lock (_sync)
            {
                owned = FindOwnedProcessLocked(process);
            }

            if (owned == null)
            {
                error = string.Empty;
                return true;
            }

            return TryTerminateOwnedProcess(owned, timeoutMilliseconds, out error);
        }

        internal bool ShutdownAndTerminateActiveProcesses(
            int timeoutMilliseconds,
            out int activeProcessCount,
            out string error)
        {
            if (timeoutMilliseconds < 0 || timeoutMilliseconds > 60_000)
            {
                throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));
            }

            OwnedProcess[] processes;
            lock (_sync)
            {
                _shutdownRequested = true;
                processes = _activeProcesses.ToArray();
            }

            activeProcessCount = processes.Length;
            var errors = new StringBuilder(256);
            var allConfirmed = true;
            long deadline = Stopwatch.GetTimestamp() +
                            (long)timeoutMilliseconds * Stopwatch.Frequency / 1000;
            for (var index = 0; index < processes.Length; index++)
            {
                OwnedProcess process = processes[index];

                bool confirmed;
                string terminationError;
                int remainingMilliseconds = GetRemainingMilliseconds(deadline);
                if (remainingMilliseconds <= 0)
                {
                    confirmed = false;
                    terminationError =
                        "The process-tree termination budget elapsed before every owned process could be checked.";
                }
                else
                {
                    try
                    {
                        confirmed = TryTerminateOwnedProcess(
                            process,
                            remainingMilliseconds,
                            out terminationError);
                    }
                    catch (Exception exception) when (
                        DataTableLubanRunner.IsRecoverableRunnerException(exception))
                    {
                        confirmed = false;
                        terminationError = exception.Message;
                    }
                }

                if (confirmed)
                {
                    confirmed = ReleaseOwnedProcess(process, out string releaseError);
                    if (!confirmed && string.IsNullOrWhiteSpace(terminationError))
                    {
                        terminationError = releaseError;
                    }
                }

                if (confirmed)
                {
                    continue;
                }

                allConfirmed = false;
                if (errors.Length > 0)
                {
                    errors.Append(' ');
                }

                errors.Append(string.IsNullOrWhiteSpace(terminationError)
                    ? "Process-tree termination could not be confirmed."
                    : terminationError.Trim());
            }

            error = errors.ToString();
            return allConfirmed;
        }

        private bool TryTerminateOwnedProcess(
            OwnedProcess owned,
            int timeoutMilliseconds,
            out string error)
        {
            lock (owned.Sync)
            {
                lock (_sync)
                {
                    if (!_activeProcesses.Contains(owned))
                    {
                        error = string.Empty;
                        return true;
                    }
                }

                try
                {
                    return _terminator(owned.Process, timeoutMilliseconds, out error);
                }
                catch (Exception exception) when (
                    DataTableLubanRunner.IsRecoverableRunnerException(exception))
                {
                    error = exception.Message;
                    return false;
                }
            }
        }

        private bool ReleaseOwnedProcess(OwnedProcess owned, out string error)
        {
            lock (owned.Sync)
            {
                lock (_sync)
                {
                    if (!_activeProcesses.Contains(owned))
                    {
                        error = string.Empty;
                        return true;
                    }
                }

                try
                {
                    _disposer(owned.Process);
                }
                catch (Exception exception) when (
                    DataTableLubanRunner.IsRecoverableRunnerException(exception))
                {
                    error = "The confirmed inspection process could not be disposed: " +
                            exception.Message;
                    return false;
                }

                lock (_sync)
                {
                    _activeProcesses.Remove(owned);
                }

                error = string.Empty;
                return true;
            }
        }

        private OwnedProcess FindOwnedProcessLocked(Process process)
        {
            for (var index = 0; index < _activeProcesses.Count; index++)
            {
                if (ReferenceEquals(_activeProcesses[index].Process, process))
                {
                    return _activeProcesses[index];
                }
            }

            return null;
        }

        private static void DisposeProcess(Process process)
        {
            process.Dispose();
        }

        private static int GetRemainingMilliseconds(long deadline)
        {
            long remainingTicks = deadline - Stopwatch.GetTimestamp();
            if (remainingTicks <= 0)
            {
                return 0;
            }

            return (int)Math.Min(
                60_000,
                Math.Max(1, remainingTicks * 1000 / Stopwatch.Frequency));
        }

        private sealed class OwnedProcess
        {
            internal OwnedProcess(Process process)
            {
                Process = process;
            }

            internal object Sync { get; } = new object();
            internal Process Process { get; }
        }
    }

    internal readonly struct DataTableLubanInspectionResult
    {
        internal DataTableLubanInspectionResult(
            bool success,
            bool cancelled,
            bool timedOut,
            int exitCode,
            DataTableLubanInspectionDocument document,
            string standardError,
            string error)
        {
            Success = success;
            Cancelled = cancelled;
            TimedOut = timedOut;
            ExitCode = exitCode;
            Document = document;
            StandardError = standardError ?? string.Empty;
            Error = error ?? string.Empty;
        }

        internal bool Success { get; }
        internal bool Cancelled { get; }
        internal bool TimedOut { get; }
        internal int ExitCode { get; }
        internal DataTableLubanInspectionDocument Document { get; }
        internal string StandardError { get; }
        internal string Error { get; }
    }

    internal static class DataTableLubanInspectionClient
    {
        private const int InspectionTimeoutMilliseconds = 120_000;
        private const int MaximumStandardErrorCharacters = 1024 * 1024;
        private static readonly DataTableLubanInspectionProcessRegistry ActiveProcesses =
            new DataTableLubanInspectionProcessRegistry(StartProcess, TerminateProcess);

        internal static bool ShutdownAndTerminateActiveProcesses(
            int timeoutMilliseconds,
            out int activeProcessCount,
            out string error)
        {
            return ActiveProcesses.ShutdownAndTerminateActiveProcesses(
                timeoutMilliseconds,
                out activeProcessCount,
                out error);
        }

        internal static async UniTask<DataTableLubanInspectionResult> InspectAsync(
            DataTableLubanSettings settings,
            CancellationToken cancellationToken)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                return new DataTableLubanInspectionResult(
                    false,
                    true,
                    false,
                    -1,
                    null,
                    string.Empty,
                    "Pipeline inspection was cancelled before process start.");
            }

            await UniTask.SwitchToMainThread();
            string toolProjectPath;
            string configurationPath;
            string profileName;
            try
            {
                toolProjectPath = DataTableLubanToolProjectLocator.ResolveToolProjectPath(settings);
                configurationPath = settings.ResolveBuildConfigurationPath();
                profileName = settings.SelectedProfileName;
            }
            catch (Exception exception) when (DataTableLubanRunner.IsRecoverableRunnerException(exception))
            {
                return new DataTableLubanInspectionResult(
                    false,
                    false,
                    false,
                    -1,
                    null,
                    string.Empty,
                    exception.Message);
            }

            return await UniTask.RunOnThreadPool(
                () => InspectBlocking(
                    toolProjectPath,
                    configurationPath,
                    profileName,
                    cancellationToken),
                cancellationToken: CancellationToken.None);
        }

        private static DataTableLubanInspectionResult InspectBlocking(
            string toolProjectPath,
            string configurationPath,
            string profileName,
            CancellationToken cancellationToken)
        {
            var standardOutput = new BoundedCapture(
                DataTableLubanInspectionProtocol.MaximumJsonCharacters);
            var standardError = new BoundedCapture(MaximumStandardErrorCharacters);
            var stopwatch = Stopwatch.StartNew();
            var process = new Process
            {
                StartInfo = CreateStartInfo(toolProjectPath, configurationPath, profileName),
            };
            CaptureReader outputReader = null;
            CaptureReader errorReader = null;
            bool processRegistered = false;
            bool cancelled = false;
            bool timedOut = false;
            bool releaseConfirmed = false;
            string terminationError = string.Empty;
            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return new DataTableLubanInspectionResult(
                        false,
                        true,
                        false,
                        -1,
                        null,
                        string.Empty,
                        "Pipeline inspection was cancelled before process start.");
                }

                DataTableLubanInspectionProcessStartOutcome startOutcome =
                    ActiveProcesses.TryStartAndRegister(process);
                if (startOutcome == DataTableLubanInspectionProcessStartOutcome.RejectedForShutdown)
                {
                    return new DataTableLubanInspectionResult(
                        false,
                        true,
                        false,
                        -1,
                        null,
                        string.Empty,
                        "Pipeline inspection was cancelled by Editor shutdown or assembly reload.");
                }

                if (startOutcome == DataTableLubanInspectionProcessStartOutcome.FailedToStart)
                {
                    return Failure("Failed to start dotnet for pipeline inspection.");
                }

                processRegistered = true;

                outputReader = new CaptureReader(process.StandardOutput, standardOutput);
                errorReader = new CaptureReader(process.StandardError, standardError);
                outputReader.Begin();
                errorReader.Begin();

                while (!process.WaitForExit(100))
                {
                    cancelled = cancellationToken.IsCancellationRequested;
                    timedOut = stopwatch.ElapsedMilliseconds >= InspectionTimeoutMilliseconds;
                    if (cancelled || timedOut)
                    {
                        releaseConfirmed |= TryTerminateProcessTree(
                            process,
                            ref terminationError);
                        break;
                    }
                }

                process.WaitForExit(10_000);
                bool readersCompleted = CompleteReaders(
                    process,
                    outputReader,
                    errorReader,
                    ref terminationError,
                    ref releaseConfirmed);
                releaseConfirmed = DataTableLubanInspectionOwnership.CanRelease(
                    cancelled || timedOut,
                    readersCompleted,
                    releaseConfirmed);

                string errorText = standardError.GetText();
                if (cancelled)
                {
                    return new DataTableLubanInspectionResult(
                        false, true, false, -1, null, errorText,
                        AppendTerminationError("Pipeline inspection was cancelled.", terminationError));
                }

                if (timedOut)
                {
                    return new DataTableLubanInspectionResult(
                        false, false, true, -1, null, errorText,
                        AppendTerminationError(
                            "Pipeline inspection exceeded the two-minute bootstrap timeout.",
                            terminationError));
                }

                if (standardOutput.WasTruncated)
                {
                    return new DataTableLubanInspectionResult(
                        false, false, false, process.ExitCode, null, errorText,
                        "Pipeline inspection JSON exceeded the 8 MiB capture limit.");
                }

                if (process.ExitCode != 0)
                {
                    string message = string.IsNullOrWhiteSpace(errorText)
                        ? "Pipeline inspection failed with exit code " + process.ExitCode + "."
                        : errorText.Trim();
                    return new DataTableLubanInspectionResult(
                        false, false, false, process.ExitCode, null, errorText, message);
                }

                if (!DataTableLubanInspectionProtocol.TryParse(
                        standardOutput.GetText(),
                        out DataTableLubanInspectionDocument document,
                        out string parseError))
                {
                    return new DataTableLubanInspectionResult(
                        false, false, false, process.ExitCode, null, errorText, parseError);
                }

                return new DataTableLubanInspectionResult(
                    true, false, false, process.ExitCode, document, errorText, string.Empty);
            }
            catch (Exception exception) when (DataTableLubanRunner.IsRecoverableRunnerException(exception))
            {
                releaseConfirmed |= TryTerminateProcessTree(process, ref terminationError);
                TryCompleteReaders(
                    process,
                    outputReader,
                    errorReader,
                    ref releaseConfirmed);
                return new DataTableLubanInspectionResult(
                    false,
                    cancellationToken.IsCancellationRequested,
                    false,
                    -1,
                    null,
                    standardError.GetText(),
                    AppendTerminationError(exception.Message, terminationError));
            }
            finally
            {
                TryCompleteReaders(
                    process,
                    outputReader,
                    errorReader,
                    ref releaseConfirmed);
                if (processRegistered)
                {
                    if (releaseConfirmed &&
                        !ActiveProcesses.ReleaseIfConfirmed(process, confirmed: true))
                    {
                        DataTableEditorDiagnostics.Publish(
                            DataTableDiagnosticLevel.Warning,
                            "A confirmed pipeline inspection process could not be released; lifecycle ownership was retained for retry.");
                    }
                }
                else
                {
                    process.Dispose();
                }
            }

            DataTableLubanInspectionResult Failure(string message)
            {
                return new DataTableLubanInspectionResult(
                    false, false, false, -1, null, standardError.GetText(), message);
            }
        }

        private static ProcessStartInfo CreateStartInfo(
            string toolProjectPath,
            string configurationPath,
            string profileName)
        {
            var arguments = new StringBuilder(512);
            AppendArgument(arguments, "run");
            AppendArgument(arguments, "--project");
            AppendArgument(arguments, toolProjectPath);
            AppendArgument(arguments, "--configuration");
            AppendArgument(arguments, "Release");
            AppendArgument(arguments, "--verbosity");
            AppendArgument(arguments, "quiet");
            AppendArgument(arguments, "--");
            AppendArgument(arguments, "pipeline");
            AppendArgument(arguments, "inspect");
            AppendArgument(arguments, "--config");
            AppendArgument(arguments, configurationPath);
            AppendArgument(arguments, "--profile");
            AppendArgument(arguments, profileName);
            AppendArgument(arguments, "--format");
            AppendArgument(arguments, "json");

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = arguments.ToString(),
                WorkingDirectory = Path.GetDirectoryName(toolProjectPath),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
            };
            startInfo.EnvironmentVariables["DOTNET_NOLOGO"] = "1";
            startInfo.EnvironmentVariables["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
            startInfo.EnvironmentVariables["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
            return startInfo;
        }

        private static bool StartProcess(Process process)
        {
            return process.Start();
        }

        private static bool TerminateProcess(
            Process process,
            int timeoutMilliseconds,
            out string error)
        {
            return DataTableLubanRunner.TryTerminateProcessTree(
                process,
                timeoutMilliseconds,
                out error);
        }

        private static bool CompleteReaders(
            Process process,
            CaptureReader outputReader,
            CaptureReader errorReader,
            ref string terminationError,
            ref bool releaseConfirmed)
        {
            if (JoinReaders(outputReader, errorReader, 10_000))
            {
                outputReader?.ThrowIfFailed();
                errorReader?.ThrowIfFailed();
                return true;
            }

            releaseConfirmed |= TryTerminateProcessTree(process, ref terminationError);
            TryCloseReaders(process);
            outputReader?.Interrupt();
            errorReader?.Interrupt();
            if (!JoinReaders(outputReader, errorReader, 10_000))
            {
                throw new InvalidOperationException(
                    "Pipeline inspection output readers did not terminate within the safety bound.");
            }

            outputReader?.ThrowIfFailed();
            errorReader?.ThrowIfFailed();
            return releaseConfirmed;
        }

        private static void TryCompleteReaders(
            Process process,
            CaptureReader outputReader,
            CaptureReader errorReader,
            ref bool releaseConfirmed)
        {
            try
            {
                if ((outputReader == null || !outputReader.IsAlive) &&
                    (errorReader == null || !errorReader.IsAlive))
                {
                    return;
                }

                string terminationError = string.Empty;
                bool confirmed = TryTerminateProcessTree(process, ref terminationError);
                releaseConfirmed |= confirmed;
                if (!confirmed &&
                    !string.IsNullOrEmpty(terminationError))
                {
                    DataTableEditorDiagnostics.Publish(
                        DataTableDiagnosticLevel.Warning,
                        "Pipeline inspection cleanup could not confirm descendant termination. " +
                        terminationError);
                }
                TryCloseReaders(process);
                outputReader?.Interrupt();
                errorReader?.Interrupt();
                JoinReaders(outputReader, errorReader, 10_000);
            }
            catch (Exception exception) when (DataTableLubanRunner.IsRecoverableRunnerException(exception))
            {
                // Inspection failure is reported by the owning result path.
            }
        }

        private static bool JoinReaders(
            CaptureReader outputReader,
            CaptureReader errorReader,
            int timeoutMilliseconds)
        {
            long deadline = Stopwatch.GetTimestamp() +
                            (long)timeoutMilliseconds * Stopwatch.Frequency / 1000;
            return JoinBeforeDeadline(outputReader, deadline) &&
                   JoinBeforeDeadline(errorReader, deadline);
        }

        private static bool JoinBeforeDeadline(CaptureReader reader, long deadline)
        {
            if (reader == null)
            {
                return true;
            }

            long remainingTicks = deadline - Stopwatch.GetTimestamp();
            int remainingMilliseconds = remainingTicks <= 0
                ? 0
                : (int)Math.Min(
                    int.MaxValue,
                    remainingTicks * 1000 / Stopwatch.Frequency);
            return reader.Join(remainingMilliseconds);
        }

        private static bool TryTerminateProcessTree(
            Process process,
            ref string terminationError)
        {
            try
            {
                if (process == null)
                {
                    return true;
                }

                bool confirmed = ActiveProcesses.TryTerminateOwnedProcess(
                    process,
                    10_000,
                    out string error);
                if (!confirmed && string.IsNullOrEmpty(terminationError))
                {
                    terminationError = string.IsNullOrWhiteSpace(error)
                        ? "Process-tree termination could not be confirmed."
                        : error;
                }

                return confirmed;
            }
            catch (Exception exception) when (DataTableLubanRunner.IsRecoverableRunnerException(exception))
            {
                if (string.IsNullOrEmpty(terminationError))
                {
                    terminationError = "Process-tree termination failed: " + exception.Message;
                }

                return false;
            }
        }

        private static string AppendTerminationError(string message, string terminationError)
        {
            if (string.IsNullOrWhiteSpace(terminationError))
            {
                return message ?? string.Empty;
            }

            return (message ?? string.Empty) + " " + terminationError;
        }

        private static void TryCloseReaders(Process process)
        {
            try
            {
                process?.StandardOutput.Close();
                process?.StandardError.Close();
            }
            catch (Exception exception) when (DataTableLubanRunner.IsRecoverableRunnerException(exception))
            {
                // Closing is best effort after bounded process termination.
            }
        }

        private static void AppendArgument(StringBuilder builder, string value)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append('"');
            var backslashes = 0;
            for (var index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (character == '\\')
                {
                    backslashes++;
                    continue;
                }

                if (character == '"')
                {
                    builder.Append('\\', backslashes * 2 + 1).Append('"');
                    backslashes = 0;
                    continue;
                }

                builder.Append('\\', backslashes).Append(character);
                backslashes = 0;
            }

            builder.Append('\\', backslashes * 2).Append('"');
        }

        private sealed class BoundedCapture
        {
            private readonly object _sync = new object();
            private readonly int _maximumCharacters;
            private readonly StringBuilder _builder;
            private bool _truncated;

            internal BoundedCapture(int maximumCharacters)
            {
                _maximumCharacters = maximumCharacters;
                _builder = new StringBuilder(Math.Min(4096, maximumCharacters));
            }

            internal bool WasTruncated
            {
                get
                {
                    lock (_sync)
                    {
                        return _truncated;
                    }
                }
            }

            internal void Append(char[] buffer, int count)
            {
                lock (_sync)
                {
                    int accepted = Math.Min(count, _maximumCharacters - _builder.Length);
                    if (accepted > 0)
                    {
                        _builder.Append(buffer, 0, accepted);
                    }

                    _truncated |= accepted != count;
                }
            }

            internal string GetText()
            {
                lock (_sync)
                {
                    return _builder.ToString();
                }
            }
        }

        private sealed class CaptureReader
        {
            private readonly StreamReader _reader;
            private readonly BoundedCapture _capture;
            private readonly Thread _thread;
            private Exception _failure;

            internal CaptureReader(StreamReader reader, BoundedCapture capture)
            {
                _reader = reader;
                _capture = capture;
                _thread = new Thread(Read)
                {
                    IsBackground = true,
                    Name = "CycloneGames.DataTable.Pipeline.Inspection.Output",
                };
            }

            internal bool IsAlive => _thread.IsAlive;

            internal void Begin()
            {
                _thread.Start();
            }

            internal bool Join(int timeoutMilliseconds)
            {
                return _thread.Join(timeoutMilliseconds);
            }

            internal void Interrupt()
            {
                if (_thread.IsAlive)
                {
                    _thread.Interrupt();
                }
            }

            internal void ThrowIfFailed()
            {
                if (_failure != null)
                {
                    throw new IOException("Failed to capture pipeline inspection output.", _failure);
                }
            }

            private void Read()
            {
                try
                {
                    var buffer = new char[4096];
                    int count;
                    while ((count = _reader.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        _capture.Append(buffer, count);
                    }
                }
                catch (ThreadInterruptedException)
                {
                    // Interruption is part of the bounded shutdown path.
                }
                catch (Exception exception)
                {
                    _failure = exception;
                }
            }
        }
    }
}
