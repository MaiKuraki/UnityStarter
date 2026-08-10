using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CycloneGames.DataTable.CodeGen
{
    internal static partial class Program
    {
        private static partial class DataTablePipeline
        {
            private const string WriterOwnerFileName = "owner.txt";
            private const string CancelRequestFileName = "cancel.request";
            private const string ActiveLubanFileName = "active-luban.txt";
            private const string ActiveLubanPendingFileName = "active-luban.pending";
            private const string ActiveLubanStageFileName = "active-luban.stage";
            private const int LockRecordMaximumBytes = 4096;
            private const int LubanProcessOutputMaximumCharacters = 1024 * 1024;
            private const int LubanStandardErrorOutputMaximumCharacters = 256 * 1024;
            private const int LubanStandardOutputMaximumCharacters =
                LubanProcessOutputMaximumCharacters - LubanStandardErrorOutputMaximumCharacters;

            private readonly struct RecordedProcessIdentity
            {
                public RecordedProcessIdentity(int processId, long startTimeUtcTicks)
                {
                    ProcessId = processId;
                    StartTimeUtcTicks = startTimeUtcTicks;
                }

                public int ProcessId { get; }
                public long StartTimeUtcTicks { get; }
            }

            private sealed class WriterLockOwner
            {
                public WriterLockOwner(
                    string runId,
                    string token,
                    RecordedProcessIdentity processIdentity,
                    string content)
                {
                    RunId = runId;
                    Token = token;
                    ProcessIdentity = processIdentity;
                    Content = content;
                }

                public string RunId { get; }
                public string Token { get; }
                public RecordedProcessIdentity ProcessIdentity { get; }
                public string Content { get; }
            }

            private sealed class ActiveLubanOwner
            {
                public ActiveLubanOwner(
                    string runId,
                    string token,
                    RecordedProcessIdentity processIdentity,
                    string content)
                {
                    RunId = runId;
                    Token = token;
                    ProcessIdentity = processIdentity;
                    Content = content;
                }

                public string RunId { get; }
                public string Token { get; }
                public RecordedProcessIdentity ProcessIdentity { get; }
                public string Content { get; }
            }

            private sealed class BoundedProcessOutputForwarder
            {
                private readonly object _syncRoot = new object();
                private readonly int _maximumCharacters;
                private int _forwardedCharacters;
                private long _omittedCharacters;

                public BoundedProcessOutputForwarder(int maximumCharacters)
                {
                    if (maximumCharacters <= 0)
                    {
                        throw new ArgumentOutOfRangeException(nameof(maximumCharacters));
                    }

                    _maximumCharacters = maximumCharacters;
                }

                public int MaximumCharacters => _maximumCharacters;

                public int ForwardedCharacters
                {
                    get
                    {
                        lock (_syncRoot)
                        {
                            return _forwardedCharacters;
                        }
                    }
                }

                public long OmittedCharacters
                {
                    get
                    {
                        lock (_syncRoot)
                        {
                            return _omittedCharacters;
                        }
                    }
                }

                public bool WasTruncated
                {
                    get
                    {
                        lock (_syncRoot)
                        {
                            return _omittedCharacters != 0;
                        }
                    }
                }

                public int Reserve(int requestedCharacters)
                {
                    if (requestedCharacters < 0)
                    {
                        throw new ArgumentOutOfRangeException(nameof(requestedCharacters));
                    }

                    lock (_syncRoot)
                    {
                        int accepted = Math.Min(
                            requestedCharacters,
                            _maximumCharacters - _forwardedCharacters);
                        _forwardedCharacters += accepted;
                        int omitted = requestedCharacters - accepted;
                        if (omitted != 0)
                        {
                            _omittedCharacters = _omittedCharacters > long.MaxValue - omitted
                                ? long.MaxValue
                                : _omittedCharacters + omitted;
                        }

                        return accepted;
                    }
                }
            }

            private sealed class PipelineWriterLock : IDisposable
            {
                private readonly string _ownerContent;
                private bool _preserve;
                private bool _disposed;

                private PipelineWriterLock(string directory, string runId, string token, string ownerContent)
                {
                    Directory = directory;
                    RunId = runId;
                    Token = token;
                    _ownerContent = ownerContent;
                }

                public string Directory { get; }
                public string RunId { get; }
                public string Token { get; }
                public string CancelRequestPath => Path.Combine(Directory, CancelRequestFileName);
                private string ActiveLubanPath => Path.Combine(Directory, ActiveLubanFileName);
                private string ActiveLubanPendingPath => Path.Combine(Directory, ActiveLubanPendingFileName);
                private string ActiveLubanStagePath => Path.Combine(Directory, ActiveLubanStageFileName);

                public static PipelineWriterLock Acquire(
                    PipelineConfiguration configuration,
                    string runId,
                    Action? afterAbsenceConfirmed = null)
                {
                    string token = Guid.NewGuid().ToString("N");
                    string directory = configuration.LockDirectory;
                    if (System.IO.Directory.Exists(directory) || File.Exists(directory))
                    {
                        throw new InvalidOperationException(
                            "Another DataTable pipeline writer is active, or a recovery lock remains: " + directory);
                    }

                    afterAbsenceConfirmed?.Invoke();

                    string ownerPath = Path.Combine(directory, WriterOwnerFileName);
                    bool ownerFileCreated = false;
                    string ownerContent = string.Empty;
                    try
                    {
                        System.IO.Directory.CreateDirectory(directory);
                    }
                    catch (IOException exception)
                    {
                        throw new InvalidOperationException(
                            "Another DataTable pipeline writer is active, or a recovery lock remains: " + directory,
                            exception);
                    }

                    try
                    {
                        AssertPhysicalContainedPath(directory, configuration.SourceRoot, "writer lock", mustExist: true);
                        using Process currentProcess = Process.GetCurrentProcess();
                        RecordedProcessIdentity processIdentity = CaptureProcessIdentity(currentProcess);
                        ownerContent =
                            "schema=CycloneGames.DataTable.WriterLock\n" +
                            "version=2\n" +
                            "run_id=" + runId + "\n" +
                            "token=" + token + "\n" +
                            "process_id=" + processIdentity.ProcessId + "\n" +
                            "process_start_utc_ticks=" + processIdentity.StartTimeUtcTicks + "\n";
                        using (var stream = new FileStream(
                                   ownerPath,
                                   FileMode.CreateNew,
                                   FileAccess.Write,
                                   FileShare.None))
                        {
                            ownerFileCreated = true;
                            byte[] bytes = Encoding.UTF8.GetBytes(ownerContent);
                            stream.Write(bytes, 0, bytes.Length);
                            stream.Flush(flushToDisk: true);
                        }

                        return new PipelineWriterLock(directory, runId, token, ownerContent);
                    }
                    catch (Exception exception)
                    {
                        if (ownerFileCreated &&
                            TryDeleteFailedOwnerIfStillOwned(ownerPath, ownerContent))
                        {
                            TryDeleteEmptyDirectory(directory);
                        }

                        if (!ownerFileCreated && exception is IOException)
                        {
                            throw new InvalidOperationException(
                                "Another DataTable pipeline writer won lock arbitration, or the writer lock " +
                                "could not be created: " + directory,
                                exception);
                        }

                        throw;
                    }

                }

                public void PreserveForRecovery()
                {
                    _preserve = true;
                }

                public bool IsCancellationRequested(CancellationToken cancellationToken)
                {
                    return cancellationToken.IsCancellationRequested || File.Exists(CancelRequestPath);
                }

                public void BeginActiveLubanLaunch()
                {
                    AssertOwnedOwnerFile();
                    if (File.Exists(ActiveLubanPendingPath) || File.Exists(ActiveLubanStagePath) ||
                        File.Exists(ActiveLubanPath) || System.IO.Directory.Exists(ActiveLubanPendingPath) ||
                        System.IO.Directory.Exists(ActiveLubanStagePath) || System.IO.Directory.Exists(ActiveLubanPath))
                    {
                        throw new InvalidOperationException(
                            "Writer lock already contains active Luban launch evidence: " + Directory);
                    }

                    try
                    {
                        WriteDurableText(ActiveLubanPendingPath, BuildPendingContent(RunId, Token), overwrite: false);
                    }
                    catch
                    {
                        if (File.Exists(ActiveLubanPendingPath))
                        {
                            AssertNotReparsePoint(ActiveLubanPendingPath, "active Luban pending marker");
                            File.Delete(ActiveLubanPendingPath);
                        }

                        throw;
                    }
                }

                public void RecordActiveLubanProcess(RecordedProcessIdentity processIdentity)
                {
                    AssertOwnedOwnerFile();
                    string pendingContent = BuildPendingContent(RunId, Token);
                    if (!File.Exists(ActiveLubanPendingPath) ||
                        !string.Equals(
                            File.ReadAllText(ActiveLubanPendingPath, Encoding.UTF8),
                            pendingContent,
                            StringComparison.Ordinal) ||
                        File.Exists(ActiveLubanPath) || System.IO.Directory.Exists(ActiveLubanPath) ||
                        File.Exists(ActiveLubanStagePath) || System.IO.Directory.Exists(ActiveLubanStagePath))
                    {
                        throw new InvalidOperationException(
                            "Active Luban launch evidence changed before process identity publication.");
                    }

                    string content = BuildActiveLubanContent(RunId, Token, processIdentity);
                    WriteDurableText(ActiveLubanStagePath, content, overwrite: false);
                    File.Move(ActiveLubanStagePath, ActiveLubanPath, overwrite: false);
                    File.Delete(ActiveLubanPendingPath);
                    if (!string.Equals(File.ReadAllText(ActiveLubanPath, Encoding.UTF8), content, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException("Active Luban identity readback verification failed.");
                    }
                }

                public void ClearActiveLubanEvidence(RecordedProcessIdentity? processIdentity)
                {
                    AssertOwnedOwnerFile();
                    string? activeContent = processIdentity.HasValue
                        ? BuildActiveLubanContent(RunId, Token, processIdentity.Value)
                        : null;
                    DeleteEvidenceIfOwned(ActiveLubanStagePath, activeContent, "active Luban staging identity");
                    DeleteEvidenceIfOwned(ActiveLubanPath, activeContent, "active Luban identity");
                    DeleteEvidenceIfOwned(
                        ActiveLubanPendingPath,
                        BuildPendingContent(RunId, Token),
                        "active Luban pending marker");
                }

                public void ThrowIfCancellationRequestedAtSafePoint(CancellationToken cancellationToken)
                {
                    if (IsCancellationRequested(cancellationToken))
                    {
                        throw new OperationCanceledException("DataTable generation was cancelled at a safe point.");
                    }
                }

                public void Dispose()
                {
                    if (_disposed || _preserve)
                    {
                        return;
                    }

                    _disposed = true;
                    string ownerPath = Path.Combine(Directory, WriterOwnerFileName);
                    if (!File.Exists(ownerPath) ||
                        !string.Equals(File.ReadAllText(ownerPath, Encoding.UTF8), _ownerContent, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "Writer lock ownership changed; preserving the lock for manual recovery: " + Directory);
                    }

                    foreach (string entry in System.IO.Directory.EnumerateFileSystemEntries(Directory))
                    {
                        string name = Path.GetFileName(entry);
                        if (name != WriterOwnerFileName && name != CancelRequestFileName &&
                            name != ActiveLubanFileName && name != ActiveLubanPendingFileName &&
                            name != ActiveLubanStageFileName)
                        {
                            throw new InvalidOperationException(
                                "Writer lock contains an unexpected entry; preserving it for recovery: " + entry);
                        }

                        AssertNotReparsePoint(entry, "writer lock entry");
                    }

                    if (File.Exists(ActiveLubanPath) || File.Exists(ActiveLubanPendingPath) ||
                        File.Exists(ActiveLubanStagePath) || System.IO.Directory.Exists(ActiveLubanPath) ||
                        System.IO.Directory.Exists(ActiveLubanPendingPath) ||
                        System.IO.Directory.Exists(ActiveLubanStagePath))
                    {
                        throw new InvalidOperationException(
                            "Active Luban process evidence remains; preserving the writer lock for recovery: " + Directory);
                    }

                    if (File.Exists(CancelRequestPath))
                    {
                        File.Delete(CancelRequestPath);
                    }

                    File.Delete(ownerPath);
                    System.IO.Directory.Delete(Directory, recursive: false);
                }

                private void AssertOwnedOwnerFile()
                {
                    string ownerPath = Path.Combine(Directory, WriterOwnerFileName);
                    if (!File.Exists(ownerPath) ||
                        !string.Equals(File.ReadAllText(ownerPath, Encoding.UTF8), _ownerContent, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            "Writer lock ownership changed; active-process evidence cannot be updated.");
                    }
                }

                private static string BuildPendingContent(string runId, string token)
                {
                    return "schema=CycloneGames.DataTable.ActiveLubanPending\n" +
                           "version=1\n" +
                           "run_id=" + runId + "\n" +
                           "token=" + token + "\n";
                }

                private static void DeleteEvidenceIfOwned(
                    string path,
                    string? expectedContent,
                    string description)
                {
                    if (!File.Exists(path) && !System.IO.Directory.Exists(path))
                    {
                        return;
                    }

                    if (expectedContent == null || !File.Exists(path))
                    {
                        throw new InvalidOperationException(description + " cannot be proven owner-safe: " + path);
                    }

                    AssertNotReparsePoint(path, description);
                    if (!string.Equals(File.ReadAllText(path, Encoding.UTF8), expectedContent, StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(description + " ownership changed: " + path);
                    }

                    File.Delete(path);
                }

                private static void TryDeleteEmptyDirectory(string directory)
                {
                    try
                    {
                        if (System.IO.Directory.Exists(directory) &&
                            !System.IO.Directory.EnumerateFileSystemEntries(directory).GetEnumerator().MoveNext())
                        {
                            System.IO.Directory.Delete(directory, recursive: false);
                        }
                    }
                    catch (Exception exception) when (IsRecoverableException(exception))
                    {
                        // The original acquisition error remains the useful failure.
                    }
                }

                private static bool TryDeleteFailedOwnerIfStillOwned(
                    string ownerPath,
                    string expectedContent)
                {
                    try
                    {
                        var info = new FileInfo(ownerPath);
                        if (!info.Exists ||
                            info.Length != Encoding.UTF8.GetByteCount(expectedContent) ||
                            info.Length > LockRecordMaximumBytes)
                        {
                            return false;
                        }

                        AssertNotReparsePoint(ownerPath, "failed writer-lock owner");
                        if (!string.Equals(
                                File.ReadAllText(ownerPath, Encoding.UTF8),
                                expectedContent,
                                StringComparison.Ordinal))
                        {
                            return false;
                        }

                        File.Delete(ownerPath);
                        return true;
                    }
                    catch (Exception exception) when (IsRecoverableException(exception))
                    {
                        return false;
                    }
                }
            }

            private static void RunLuban(
                PipelineConfiguration configuration,
                PipelineProfile profile,
                PipelineIdentity identity,
                PipelineTransaction transaction,
                PipelineWriterLock writerLock,
                CancellationToken cancellationToken)
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = identity.UseDotNetHost ? "dotnet" : identity.LubanExecutablePath,
                    WorkingDirectory = configuration.SourceRoot,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                if (identity.UseDotNetHost)
                {
                    startInfo.ArgumentList.Add(identity.LubanExecutablePath);
                }

                AddLubanArguments(startInfo, configuration, profile, transaction);
                Console.WriteLine("[DataTable.Pipeline] Generating candidate for profile '" + profile.Name + "'.");
                using var process = new Process { StartInfo = startInfo };
                writerLock.BeginActiveLubanLaunch();
                bool processStarted = false;
                bool identityCaptured = false;
                bool identityRecorded = false;
                RecordedProcessIdentity processIdentity = default;
                try
                {
                    if (!process.Start())
                    {
                        writerLock.ClearActiveLubanEvidence(processIdentity: null);
                        throw new InvalidOperationException("Failed to start the approved Luban executable.");
                    }

                    processStarted = true;
                    try
                    {
                        processIdentity = CaptureProcessIdentity(process);
                        identityCaptured = true;
                        writerLock.RecordActiveLubanProcess(processIdentity);
                        identityRecorded = true;
                    }
                    catch (Exception identityException) when (IsRecoverableException(identityException))
                    {
                        try
                        {
                            KillProcessTree(process);
                            writerLock.ClearActiveLubanEvidence(
                                identityCaptured ? processIdentity : null);
                        }
                        catch (Exception cleanupException) when (IsRecoverableException(cleanupException))
                        {
                            throw new RecoveryRequiredException(
                                "Luban started, but its process identity could not be recorded or safely cleared. " +
                                "Identity error: " + identityException.Message +
                                " Cleanup error: " + cleanupException.Message,
                                cleanupException);
                        }

                        throw new InvalidOperationException(
                            "Luban process identity publication failed after the process tree was terminated.",
                            identityException);
                    }

                    var standardOutputForwarder = new BoundedProcessOutputForwarder(
                        LubanStandardOutputMaximumCharacters);
                    var standardErrorForwarder = new BoundedProcessOutputForwarder(
                        LubanStandardErrorOutputMaximumCharacters);
                    Task stdout = PumpProcessStreamAsync(
                        process.StandardOutput,
                        Console.Out,
                        standardOutputForwarder);
                    Task stderr = PumpProcessStreamAsync(
                        process.StandardError,
                        Console.Error,
                        standardErrorForwarder);
                    long deadline = Stopwatch.GetTimestamp() +
                                    (long)configuration.ProcessTimeoutSeconds * Stopwatch.Frequency;
                    while (!process.WaitForExit(100))
                    {
                        if (writerLock.IsCancellationRequested(cancellationToken))
                        {
                            KillProcessTree(process);
                            WaitForProcessReaders(
                                stdout,
                                stderr,
                                standardOutputForwarder,
                                standardErrorForwarder);
                            throw new OperationCanceledException("DataTable generation was cancelled before publication.");
                        }

                        if (Stopwatch.GetTimestamp() >= deadline)
                        {
                            KillProcessTree(process);
                            WaitForProcessReaders(
                                stdout,
                                stderr,
                                standardOutputForwarder,
                                standardErrorForwarder);
                            throw new TimeoutException(
                                $"Luban exceeded the configured {configuration.ProcessTimeoutSeconds}-second timeout.");
                        }
                    }

                    WaitForProcessReaders(
                        stdout,
                        stderr,
                        standardOutputForwarder,
                        standardErrorForwarder);
                    if (process.ExitCode != 0)
                    {
                        throw new InvalidOperationException("Luban failed with exit code " + process.ExitCode + ".");
                    }
                }
                finally
                {
                    if (!processStarted)
                    {
                        writerLock.ClearActiveLubanEvidence(processIdentity: null);
                    }
                    else if (identityRecorded && IsProcessConfirmedExited(process, processIdentity))
                    {
                        writerLock.ClearActiveLubanEvidence(processIdentity);
                    }
                }
            }

            private static void AddLubanArguments(
                ProcessStartInfo startInfo,
                PipelineConfiguration configuration,
                PipelineProfile profile,
                PipelineTransaction transaction)
            {
                string[] arguments =
                {
                    "-t", profile.Name,
                    "-c", profile.CodeTarget,
                    "-d", profile.DataTarget,
                    "--conf", configuration.LubanConfigurationPath,
                    "-x", "lineEnding=" + profile.LineEnding,
                    "-x", "outputCodeDir=" + transaction.CandidateCodeRoot,
                    "-x", "outputDataDir=" + transaction.CandidateDataRoot,
                    "-x", "outputSaver." + profile.CodeTarget + ".cleanUpOutputDir=true",
                    "-x", "outputSaver." + profile.DataTarget + ".cleanUpOutputDir=true",
                };
                foreach (string argument in arguments)
                {
                    startInfo.ArgumentList.Add(argument);
                }

                if (configuration.CustomTemplateRoot.Length != 0)
                {
                    startInfo.ArgumentList.Add("--customTemplateDir");
                    startInfo.ArgumentList.Add(configuration.CustomTemplateRoot);
                }
            }

            private static async Task PumpProcessStreamAsync(
                TextReader reader,
                TextWriter destination,
                BoundedProcessOutputForwarder forwarder)
            {
                char[] buffer = new char[4096];
                int count;
                while ((count = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false)) != 0)
                {
                    int accepted = forwarder.Reserve(count);
                    if (accepted != 0)
                    {
                        await destination.WriteAsync(buffer, 0, accepted).ConfigureAwait(false);
                    }
                }
            }

            private static void WaitForProcessReaders(
                Task stdout,
                Task stderr,
                BoundedProcessOutputForwarder standardOutputForwarder,
                BoundedProcessOutputForwarder standardErrorForwarder)
            {
                if (!Task.WaitAll(new[] { stdout, stderr }, TimeSpan.FromSeconds(10)))
                {
                    throw new InvalidOperationException("Timed out while draining Luban process output.");
                }

                if (standardOutputForwarder.WasTruncated || standardErrorForwarder.WasTruncated)
                {
                    Console.Error.WriteLine(
                        "[DataTable.Pipeline] Luban output forwarding reached at least one partition of its " +
                        LubanProcessOutputMaximumCharacters +
                        "-character combined limit with a reserved stderr partition; stdout forwarded " +
                        standardOutputForwarder.ForwardedCharacters + "/" +
                        standardOutputForwarder.MaximumCharacters + " and omitted " +
                        standardOutputForwarder.OmittedCharacters + ", stderr forwarded " +
                        standardErrorForwarder.ForwardedCharacters + "/" +
                        standardErrorForwarder.MaximumCharacters + " and omitted " +
                        standardErrorForwarder.OmittedCharacters +
                        " character(s). Both streams were fully drained.");
                }
            }

            private static void KillProcessTree(Process process)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        if (!process.WaitForExit(10000))
                        {
                            throw new InvalidOperationException(
                                "The Luban process tree did not terminate within the bounded wait.");
                        }
                    }
                }
                catch (Exception exception) when (IsRecoverableException(exception))
                {
                    throw new RecoveryRequiredException(
                        "Failed to terminate the Luban process tree; retain the writer lock and inspect descendants.",
                        exception);
                }
            }

            private static RecordedProcessIdentity CaptureProcessIdentity(Process process)
            {
                if (process == null)
                {
                    throw new ArgumentNullException(nameof(process));
                }

                return new RecordedProcessIdentity(
                    process.Id,
                    process.StartTime.ToUniversalTime().Ticks);
            }

            private static bool IsProcessConfirmedExited(
                Process process,
                RecordedProcessIdentity expectedIdentity)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        return false;
                    }

                    return true;
                }
                catch (InvalidOperationException)
                {
                    return true;
                }
                catch (Exception exception) when (IsRecoverableException(exception))
                {
                    throw new RecoveryRequiredException(
                        "Could not confirm that the recorded Luban process exited. " +
                        "Preserve active-process evidence for recovery. PID: " + expectedIdentity.ProcessId,
                        exception);
                }
            }

            private static string BuildActiveLubanContent(
                string runId,
                string token,
                RecordedProcessIdentity processIdentity)
            {
                return "schema=CycloneGames.DataTable.ActiveLuban\n" +
                       "version=1\n" +
                       "run_id=" + runId + "\n" +
                       "token=" + token + "\n" +
                       "process_id=" + processIdentity.ProcessId + "\n" +
                       "process_start_utc_ticks=" + processIdentity.StartTimeUtcTicks + "\n";
            }

            private static WriterLockOwner ReadWriterLockOwner(string ownerPath)
            {
                Dictionary<string, string> values = ReadStrictLockRecord(
                    ownerPath,
                    "writer-lock owner",
                    "schema", "version", "run_id", "token", "process_id", "process_start_utc_ticks");
                if (RequireLockValue(values, "schema", "writer-lock owner") !=
                        "CycloneGames.DataTable.WriterLock" ||
                    RequireLockValue(values, "version", "writer-lock owner") != "2")
                {
                    throw new InvalidOperationException("Writer-lock owner schema or version is unsupported.");
                }

                string runId = RequireLockValue(values, "run_id", "writer-lock owner");
                string token = RequireLockValue(values, "token", "writer-lock owner");
                ValidateRunId(runId);
                ValidateHexToken(token, "writer-lock token");
                RecordedProcessIdentity identity = ParseRecordedProcessIdentity(values, "writer-lock owner");
                return new WriterLockOwner(runId, token, identity, File.ReadAllText(ownerPath, Encoding.UTF8));
            }

            private static ActiveLubanOwner ReadActiveLubanOwner(string path)
            {
                Dictionary<string, string> values = ReadStrictLockRecord(
                    path,
                    "active Luban identity",
                    "schema", "version", "run_id", "token", "process_id", "process_start_utc_ticks");
                if (RequireLockValue(values, "schema", "active Luban identity") !=
                        "CycloneGames.DataTable.ActiveLuban" ||
                    RequireLockValue(values, "version", "active Luban identity") != "1")
                {
                    throw new InvalidOperationException("Active Luban identity schema or version is unsupported.");
                }

                string runId = RequireLockValue(values, "run_id", "active Luban identity");
                string token = RequireLockValue(values, "token", "active Luban identity");
                ValidateRunId(runId);
                ValidateHexToken(token, "active Luban token");
                RecordedProcessIdentity identity = ParseRecordedProcessIdentity(values, "active Luban identity");
                return new ActiveLubanOwner(runId, token, identity, File.ReadAllText(path, Encoding.UTF8));
            }

            private static Dictionary<string, string> ReadStrictLockRecord(
                string path,
                string description,
                params string[] knownKeys)
            {
                var info = new FileInfo(path);
                if (!info.Exists || info.Length <= 0 || info.Length > LockRecordMaximumBytes)
                {
                    throw new InvalidOperationException(description + " is missing, empty, or oversized: " + path);
                }

                AssertNotReparsePoint(path, description);
                byte[] bytes = File.ReadAllBytes(path);
                RejectUtf8Bom(bytes, path);
                string text = new UTF8Encoding(false, true).GetString(bytes);
                if (text.IndexOf('\r') >= 0 || text.Any(static character =>
                        character != '\n' && (character < 0x20 || character > 0x7e)))
                {
                    throw new InvalidOperationException(description + " must contain printable ASCII and LF only.");
                }

                var known = new HashSet<string>(knownKeys, StringComparer.Ordinal);
                var values = new Dictionary<string, string>(StringComparer.Ordinal);
                string[] lines = text.Split('\n');
                if (lines.Length > 32)
                {
                    throw new InvalidOperationException(description + " contains too many lines.");
                }

                foreach (string line in lines)
                {
                    if (line.Length == 0)
                    {
                        continue;
                    }

                    int separator = line.IndexOf('=');
                    if (separator <= 0 || separator == line.Length - 1)
                    {
                        throw new InvalidOperationException(description + " contains a malformed entry.");
                    }

                    string key = line.Substring(0, separator);
                    string value = line.Substring(separator + 1);
                    if (!known.Contains(key) || !values.TryAdd(key, value))
                    {
                        throw new InvalidOperationException(
                            description + " contains an unknown or duplicate key: " + key);
                    }
                }

                if (values.Count != known.Count)
                {
                    throw new InvalidOperationException(description + " is missing one or more required keys.");
                }

                return values;
            }

            private static RecordedProcessIdentity ParseRecordedProcessIdentity(
                Dictionary<string, string> values,
                string description)
            {
                if (!int.TryParse(RequireLockValue(values, "process_id", description), out int processId) ||
                    processId <= 0 ||
                    !long.TryParse(
                        RequireLockValue(values, "process_start_utc_ticks", description),
                        out long startTimeUtcTicks) ||
                    startTimeUtcTicks <= 0)
                {
                    throw new InvalidOperationException(description + " contains an invalid process identity.");
                }

                return new RecordedProcessIdentity(processId, startTimeUtcTicks);
            }

            private static string RequireLockValue(
                Dictionary<string, string> values,
                string key,
                string description)
            {
                if (!values.TryGetValue(key, out string? value) || value.Length == 0)
                {
                    throw new InvalidOperationException(description + " is missing key: " + key);
                }

                return value;
            }

            private static void ValidateHexToken(string token, string description)
            {
                if (token.Length != 32 || token.Any(static character => !Uri.IsHexDigit(character)))
                {
                    throw new InvalidOperationException(description + " is not a 32-character hexadecimal value.");
                }
            }

            private static void AssertRecordedProcessStopped(
                RecordedProcessIdentity processIdentity,
                string description)
            {
                Process process;
                try
                {
                    process = Process.GetProcessById(processIdentity.ProcessId);
                }
                catch (ArgumentException)
                {
                    return;
                }

                using (process)
                {
                    long actualStartTimeUtcTicks;
                    try
                    {
                        if (process.HasExited)
                        {
                            return;
                        }

                        actualStartTimeUtcTicks = process.StartTime.ToUniversalTime().Ticks;
                    }
                    catch (InvalidOperationException)
                    {
                        return;
                    }
                    catch (Exception exception) when (IsRecoverableException(exception))
                    {
                        throw new InvalidOperationException(
                            "Recovery cannot verify whether " + description + " is still alive.",
                            exception);
                    }

                    if (actualStartTimeUtcTicks == processIdentity.StartTimeUtcTicks)
                    {
                        throw new InvalidOperationException(
                            "Recovery refuses to run while " + description + " is alive. PID: " +
                            processIdentity.ProcessId);
                    }

                    // The PID was reused after the recorded process terminated.
                }
            }
        }
    }
}
