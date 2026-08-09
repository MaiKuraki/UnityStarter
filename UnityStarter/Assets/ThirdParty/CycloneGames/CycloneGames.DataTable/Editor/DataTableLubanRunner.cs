using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.DataTable.Unity.Editor
{
    internal enum DataTableLubanOperation
    {
        Generate,
        Check,
        Recover,
    }

    internal sealed class DataTableLubanProfile
    {
        public DataTableLubanProfile(
            string toolProjectPath,
            string buildConfigurationPath,
            string profileName,
            int timeoutMilliseconds,
            bool refreshAssetsAfterSuccess,
            int maximumCapturedOutputCharacters)
        {
            ToolProjectPath = ValidateProjectPath(toolProjectPath);
            BuildConfigurationPath = ValidateConfigurationPath(buildConfigurationPath);
            ProfileName = ValidatePortableName(profileName, nameof(profileName), 128);
            if (timeoutMilliseconds < 1000 || timeoutMilliseconds > 86_400_000)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timeoutMilliseconds),
                    "Timeout must be between one second and 24 hours.");
            }

            if (maximumCapturedOutputCharacters < 4096 || maximumCapturedOutputCharacters > 16 * 1024 * 1024)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maximumCapturedOutputCharacters),
                    "Captured output must be between 4 KiB and 16 MiB characters.");
            }

            TimeoutMilliseconds = timeoutMilliseconds;
            RefreshAssetsAfterSuccess = refreshAssetsAfterSuccess;
            MaximumCapturedOutputCharacters = maximumCapturedOutputCharacters;
            ToolWorkingDirectory = Path.GetDirectoryName(ToolProjectPath);
            WriterLockDirectory = Path.Combine(
                Path.GetDirectoryName(BuildConfigurationPath),
                ".cyclonegames-datatable-writer.lock");
        }

        public string ToolProjectPath { get; }
        public string BuildConfigurationPath { get; }
        public string ProfileName { get; }
        public int TimeoutMilliseconds { get; }
        public bool RefreshAssetsAfterSuccess { get; }
        public int MaximumCapturedOutputCharacters { get; }
        internal string ToolWorkingDirectory { get; }
        internal string WriterLockDirectory { get; }

        private static string ValidateProjectPath(string path)
        {
            string fullPath = ValidateContainedRepositoryPath(path, nameof(path));
            if (!File.Exists(fullPath) ||
                !string.Equals(Path.GetExtension(fullPath), ".csproj", StringComparison.OrdinalIgnoreCase))
            {
                throw new FileNotFoundException("DataTable pipeline .csproj was not found.", fullPath);
            }

            return fullPath;
        }

        private static string ValidateConfigurationPath(string path)
        {
            string fullPath = ValidateContainedRepositoryPath(path, nameof(path));
            if (!File.Exists(fullPath) ||
                !string.Equals(Path.GetFileName(fullPath), "build_config.ini", StringComparison.Ordinal))
            {
                throw new FileNotFoundException("DataTable build_config.ini was not found.", fullPath);
            }

            return fullPath;
        }

        private static string ValidateContainedRepositoryPath(string path, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("Path is required.", parameterName);
            }

            string fullPath = Path.GetFullPath(path);
            string repositoryRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..", ".."));
            string prefix = repositoryRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(
                    prefix,
                    Application.platform == RuntimePlatform.WindowsEditor
                        ? StringComparison.OrdinalIgnoreCase
                        : StringComparison.Ordinal))
            {
                throw new ArgumentException("Path must be contained by the repository root: " + fullPath, parameterName);
            }

            string probe = fullPath;
            while (!string.Equals(
                       probe,
                       repositoryRoot,
                       Application.platform == RuntimePlatform.WindowsEditor
                           ? StringComparison.OrdinalIgnoreCase
                           : StringComparison.Ordinal))
            {
                if ((File.Exists(probe) || Directory.Exists(probe)) &&
                    (File.GetAttributes(probe) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new ArgumentException("Path traverses a reparse point: " + probe, parameterName);
                }

                probe = Path.GetDirectoryName(probe) ??
                        throw new ArgumentException("Path did not reach the repository root.", parameterName);
            }

            return fullPath;
        }

        private static string ValidatePortableName(string value, string parameterName, int maximumCharacters)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > maximumCharacters)
            {
                throw new ArgumentException("Profile name is empty or too long.", parameterName);
            }

            for (var index = 0; index < value.Length; index++)
            {
                char character = value[index];
                bool supported = character >= 'A' && character <= 'Z' ||
                                 character >= 'a' && character <= 'z' ||
                                 character >= '0' && character <= '9' ||
                                 character == '_' || character == '-' || character == '.';
                if (!supported)
                {
                    throw new ArgumentException("Profile name contains unsupported characters.", parameterName);
                }
            }

            return value;
        }
    }

    internal readonly struct DataTableLubanCommand
    {
        private DataTableLubanCommand(
            DataTableLubanOperation operation,
            DataTableLubanProfile profile,
            string recoveryRunId)
        {
            Operation = operation;
            Profile = profile ?? throw new ArgumentNullException(nameof(profile));
            RecoveryRunId = recoveryRunId ?? string.Empty;
        }

        public DataTableLubanOperation Operation { get; }
        public DataTableLubanProfile Profile { get; }
        public string RecoveryRunId { get; }

        public static DataTableLubanCommand Generate(DataTableLubanProfile profile)
        {
            return new DataTableLubanCommand(DataTableLubanOperation.Generate, profile, string.Empty);
        }

        public static DataTableLubanCommand Check(DataTableLubanProfile profile)
        {
            return new DataTableLubanCommand(DataTableLubanOperation.Check, profile, string.Empty);
        }

        public static DataTableLubanCommand Recover(DataTableLubanProfile profile, string runId)
        {
            if (string.IsNullOrEmpty(runId) || runId.Length != 32)
            {
                throw new ArgumentException("Recovery run ID must contain 32 hexadecimal characters.", nameof(runId));
            }

            for (var index = 0; index < runId.Length; index++)
            {
                if (!Uri.IsHexDigit(runId[index]))
                {
                    throw new ArgumentException("Recovery run ID must contain 32 hexadecimal characters.", nameof(runId));
                }
            }

            return new DataTableLubanCommand(DataTableLubanOperation.Recover, profile, runId);
        }
    }

    internal readonly struct DataTableLubanRunResult
    {
        internal DataTableLubanRunResult(
            bool success,
            bool cancelled,
            bool timedOut,
            bool recoveryRequired,
            bool outputTruncated,
            int exitCode,
            long durationMilliseconds,
            string standardOutput,
            string standardError,
            string recoveryRunId,
            string errorMessage)
        {
            Success = success && !cancelled && !timedOut && !recoveryRequired;
            Cancelled = cancelled;
            TimedOut = timedOut;
            RecoveryRequired = recoveryRequired;
            OutputTruncated = outputTruncated;
            ExitCode = exitCode;
            DurationMilliseconds = durationMilliseconds;
            StandardOutput = standardOutput ?? string.Empty;
            StandardError = standardError ?? string.Empty;
            RecoveryRunId = recoveryRunId ?? string.Empty;
            ErrorMessage = errorMessage ?? string.Empty;
        }

        public bool Success { get; }
        public bool Cancelled { get; }
        public bool TimedOut { get; }
        public bool RecoveryRequired { get; }
        public bool OutputTruncated { get; }
        public int ExitCode { get; }
        public long DurationMilliseconds { get; }
        public string StandardOutput { get; }
        public string StandardError { get; }
        public string RecoveryRunId { get; }
        public string ErrorMessage { get; }
    }

    internal static class DataTableLubanRunner
    {
        private const int CancellationGraceMilliseconds = 30_000;
        private static readonly object ActiveProcessSync = new object();
        private static Process _activeProcess;
        private static DataTableLubanProfile _activeProfile;
        private static DataTableLubanOperation _activeOperation;
        private static bool _runReserved;
        private static bool _cancelRequested;
        private static bool _lifecycleShutdownRequested;
        private static bool _activeProcessStarted;
        private static long _activeRunId;
        private static long _nextRunId;
        private static long _stateRevision;
        private static DataTableLubanRunnerState _currentState =
            DataTableLubanRunnerState.CreateIdle(0);

        static DataTableLubanRunner()
        {
            AssemblyReloadEvents.beforeAssemblyReload += RequestCancellationForShutdown;
            EditorApplication.quitting += RequestCancellationForShutdown;
        }

        internal static DataTableLubanRunnerState CurrentState
        {
            get
            {
                lock (ActiveProcessSync)
                {
                    return _currentState;
                }
            }
        }

        internal static bool IsRunning
        {
            get
            {
                lock (ActiveProcessSync)
                {
                    return _runReserved;
                }
            }
        }

        internal static bool CancelActiveRun()
        {
            DataTableLubanProfile profile;
            DataTableLubanOperation operation;
            int processId;
            lock (ActiveProcessSync)
            {
                if (!_runReserved)
                {
                    return false;
                }

                if (!_currentState.CanCancel)
                {
                    return _cancelRequested &&
                           _currentState.Phase == DataTableLubanRunnerPhase.CancellationRequested;
                }

                _cancelRequested = true;
                profile = _activeProfile;
                operation = _activeOperation;
                processId = _currentState.ProcessId;
                SetActiveStateLocked(
                    DataTableLubanRunnerPhase.CancellationRequested,
                    processId);
            }

            DataTableEditorDiagnostics.Publish(
                DataTableDiagnosticLevel.Warning,
                BuildLifecycleMessage(
                    "Cancellation requested",
                    operation,
                    profile,
                    processId));
            TryWriteCancellationRequest(profile);
            return true;
        }

        internal static async UniTask<DataTableLubanRunResult> ExecuteAsync(
            DataTableLubanCommand command,
            CancellationToken cancellationToken = default)
        {
            await UniTask.SwitchToMainThread();
            if (command.Profile == null)
            {
                DataTableEditorDiagnostics.Publish(
                    DataTableDiagnosticLevel.Error,
                    "DataTable Luban operation was blocked because its immutable profile is missing.");
                throw new ArgumentException("Command must contain an immutable profile.", nameof(command));
            }

            if (cancellationToken.IsCancellationRequested)
            {
                DataTableLubanRunResult cancelled = CreateCancelledBeforeStart();
                RecordStandaloneResult(command, cancelled);
                DataTableEditorDiagnostics.Publish(
                    DataTableDiagnosticLevel.Warning,
                    BuildLifecycleMessage(
                        "Operation cancelled before process reservation",
                        command.Operation,
                        command.Profile,
                        0));
                return cancelled;
            }

            ProcessStartInfo startInfo = CreateStartInfo(command);
            if (!TryBeginRun(command, out long runId))
            {
                bool lifecycleShutdownRequested = IsLifecycleShutdownRequested();
                DataTableLubanRunResult blocked = CreateFailure(
                    lifecycleShutdownRequested
                        ? "The DataTable Editor lifecycle is shutting down; new pipeline commands are rejected."
                        : "Another DataTable Editor pipeline command is already running.");
                DataTableLubanRunnerState active = CurrentState;
                DataTableEditorDiagnostics.Publish(
                    DataTableDiagnosticLevel.Warning,
                    BuildLifecycleMessage(
                        lifecycleShutdownRequested
                            ? "Operation blocked because Editor shutdown or assembly reload has begun"
                            : "Operation blocked by the active single-writer reservation (active phase " +
                              active.Phase + ")",
                        command.Operation,
                        command.Profile,
                        active.ProcessId));
                return blocked;
            }

            DataTableEditorDiagnostics.Publish(
                DataTableDiagnosticLevel.Info,
                BuildLifecycleMessage(
                    "Operation accepted",
                    command.Operation,
                    command.Profile,
                    0));
            DataTableEditorDiagnostics.Publish(
                DataTableDiagnosticLevel.Info,
                BuildLifecycleMessage(
                    "Preparing isolated Editor execution",
                    command.Operation,
                    command.Profile,
                    0));
            bool autoRefreshSuspended = false;
            DataTableLubanRunResult completedResult = default;
            bool hasCompletedResult = false;
            try
            {
                AssetDatabase.DisallowAutoRefresh();
                autoRefreshSuspended = true;
                DataTableLubanRunResult result = await UniTask.RunOnThreadPool(
                    () => RunProcess(runId, command, startInfo, cancellationToken),
                    cancellationToken: CancellationToken.None);
                await UniTask.SwitchToMainThread();
                completedResult = result;
                hasCompletedResult = true;
            }
            catch (Exception exception) when (IsRecoverableRunnerException(exception))
            {
                await UniTask.SwitchToMainThread();
                completedResult = CreateFailure(exception.Message);
                hasCompletedResult = true;
                DataTableEditorDiagnostics.PublishException(
                    DataTableDiagnosticLevel.Error,
                    exception,
                    BuildLifecycleMessage(
                        "Editor orchestration failed",
                        command.Operation,
                        command.Profile,
                        0));
            }
            finally
            {
                bool orchestrationFinalizationCompleted = false;
                try
                {
                    await UniTask.SwitchToMainThread();
                    if (autoRefreshSuspended)
                    {
                        try
                        {
                            AssetDatabase.AllowAutoRefresh();
                        }
                        catch (Exception exception) when (IsRecoverableRunnerException(exception))
                        {
                            if (hasCompletedResult)
                            {
                                completedResult = CreateEditorFailure(
                                    completedResult,
                                    "AssetDatabase auto-refresh could not be restored: " +
                                    exception.Message);
                            }

                            DataTableEditorDiagnostics.PublishException(
                                DataTableDiagnosticLevel.Error,
                                exception,
                                BuildLifecycleMessage(
                                    "Failed to restore AssetDatabase auto-refresh",
                                    command.Operation,
                                    command.Profile,
                                    0));
                        }
                    }

                    orchestrationFinalizationCompleted = true;
                }
                finally
                {
                    if (!orchestrationFinalizationCompleted || !hasCompletedResult)
                    {
                        AbandonRun(runId, command);
                    }
                }
            }

            bool finalStatePublished = false;
            try
            {
                if (ShouldRefreshAssets(command, completedResult))
                {
                    try
                    {
                        AssetDatabase.Refresh();
                        DataTableEditorDiagnostics.Publish(
                            DataTableDiagnosticLevel.Info,
                            BuildLifecycleMessage(
                                "AssetDatabase refreshed successfully after the operation",
                                command.Operation,
                                command.Profile,
                                0));
                    }
                    catch (Exception exception) when (IsRecoverableRunnerException(exception))
                    {
                        completedResult = CreateEditorFailure(
                            completedResult,
                            "The pipeline process succeeded, but AssetDatabase.Refresh failed: " +
                            exception.Message);
                        DataTableEditorDiagnostics.PublishException(
                            DataTableDiagnosticLevel.Error,
                            exception,
                            BuildLifecycleMessage(
                                "AssetDatabase refresh failed after a successful pipeline process",
                                command.Operation,
                                command.Profile,
                                0));
                    }
                }

                CompleteRun(runId, command, completedResult);
                finalStatePublished = true;
                LogResult(command, completedResult);
                return completedResult;
            }
            finally
            {
                if (!finalStatePublished)
                {
                    AbandonRun(runId, command);
                }
            }
        }

        internal static bool ShouldRefreshAssets(
            DataTableLubanCommand command,
            DataTableLubanRunResult result)
        {
            return result.Success &&
                   command.Profile != null &&
                   command.Profile.RefreshAssetsAfterSuccess &&
                   command.Operation != DataTableLubanOperation.Check;
        }

        internal static ProcessStartInfo CreateStartInfo(DataTableLubanCommand command)
        {
            var arguments = new StringBuilder(512);
            AppendArgument(arguments, "run");
            AppendArgument(arguments, "--project");
            AppendArgument(arguments, command.Profile.ToolProjectPath);
            AppendArgument(arguments, "--configuration");
            AppendArgument(arguments, "Release");
            AppendArgument(arguments, "--");
            AppendArgument(arguments, "pipeline");
            AppendArgument(arguments, command.Operation.ToString().ToLowerInvariant());
            AppendArgument(arguments, "--config");
            AppendArgument(arguments, command.Profile.BuildConfigurationPath);
            if (command.Operation == DataTableLubanOperation.Recover)
            {
                AppendArgument(arguments, "--run-id");
                AppendArgument(arguments, command.RecoveryRunId);
            }
            else
            {
                AppendArgument(arguments, "--profile");
                AppendArgument(arguments, command.Profile.ProfileName);
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = arguments.ToString(),
                WorkingDirectory = command.Profile.ToolWorkingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
                CreateNoWindow = true,
            };
            startInfo.EnvironmentVariables["DOTNET_NOLOGO"] = "1";
            startInfo.EnvironmentVariables["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
            startInfo.EnvironmentVariables["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
            return startInfo;
        }

        private static DataTableLubanRunResult RunProcess(
            long runId,
            DataTableLubanCommand command,
            ProcessStartInfo startInfo,
            CancellationToken cancellationToken)
        {
            var outputBudget = new CapturedOutputBudget(command.Profile.MaximumCapturedOutputCharacters);
            var output = new BoundedTextBuffer(outputBudget);
            var error = new BoundedTextBuffer(outputBudget);
            var stopwatch = Stopwatch.StartNew();
            using var process = new Process { StartInfo = startInfo };
            if (!TryAttachActiveProcess(runId, process))
            {
                DataTableEditorDiagnostics.Publish(
                    DataTableDiagnosticLevel.Error,
                    BuildLifecycleMessage(
                        "Process start blocked because the run reservation was lost",
                        command.Operation,
                        command.Profile,
                        0));
                return CreateFailure("The DataTable pipeline run reservation was lost before process start.");
            }

            ProcessOutputReader outputReader = null;
            ProcessOutputReader errorReader = null;
            bool timedOut = false;
            bool cancellationRequested = false;
            bool cancellationRequestWritten = false;
            bool terminationUnconfirmed = false;
            long cancellationDeadline = 0;
            try
            {
                if (cancellationToken.IsCancellationRequested || GetCancelRequested())
                {
                    DataTableEditorDiagnostics.Publish(
                        DataTableDiagnosticLevel.Warning,
                        BuildLifecycleMessage(
                            "Operation cancelled before dotnet process start",
                            command.Operation,
                            command.Profile,
                            0));
                    return CreateCancelledBeforeStart();
                }

                DataTableEditorDiagnostics.Publish(
                    DataTableDiagnosticLevel.Info,
                    BuildLifecycleMessage(
                        "Starting dotnet process",
                        command.Operation,
                        command.Profile,
                        0));
                if (!TryStartAttachedProcess(process))
                {
                    bool shutdownRequested = IsShutdownRequested(runId, process);
                    DataTableEditorDiagnostics.Publish(
                        shutdownRequested
                            ? DataTableDiagnosticLevel.Warning
                            : DataTableDiagnosticLevel.Error,
                        BuildLifecycleMessage(
                            shutdownRequested
                                ? "Process start rejected because Editor shutdown or assembly reload began"
                                : "Process start failed",
                            command.Operation,
                            command.Profile,
                            0));
                    return CreateFailure(
                        shutdownRequested
                            ? "The DataTable pipeline process was not started because Editor shutdown or assembly reload began."
                            : "Failed to start dotnet.");
                }

                int processId = process.Id;
                MarkProcessRunning(runId, processId);
                DataTableEditorDiagnostics.Publish(
                    DataTableDiagnosticLevel.Info,
                    BuildLifecycleMessage(
                        "dotnet process started",
                        command.Operation,
                        command.Profile,
                        processId));
                outputReader = StartReader(process.StandardOutput, output);
                errorReader = StartReader(process.StandardError, error);
                while (!process.WaitForExit(100))
                {
                    bool shouldCancel = cancellationToken.IsCancellationRequested || GetCancelRequested();
                    if (!timedOut && stopwatch.ElapsedMilliseconds >= command.Profile.TimeoutMilliseconds)
                    {
                        timedOut = true;
                        shouldCancel = true;
                    }

                    if (shouldCancel && !cancellationRequested)
                    {
                        cancellationRequested = true;
                        MarkCancellationRequested(runId, processId);
                        DataTableEditorDiagnostics.Publish(
                            timedOut
                                ? DataTableDiagnosticLevel.Error
                                : DataTableDiagnosticLevel.Warning,
                            BuildLifecycleMessage(
                                timedOut
                                    ? "Process timeout reached; safe cancellation requested"
                                    : "Cancellation acknowledged by the process worker",
                                command.Operation,
                                command.Profile,
                                processId));
                        cancellationRequestWritten = TryWriteCancellationRequest(command.Profile);
                        if (cancellationRequestWritten)
                        {
                            DataTableEditorDiagnostics.Publish(
                                DataTableDiagnosticLevel.Info,
                                BuildLifecycleMessage(
                                    "Safe cancellation request persisted",
                                    command.Operation,
                                    command.Profile,
                                    processId));
                        }

                        cancellationDeadline = stopwatch.ElapsedMilliseconds + CancellationGraceMilliseconds;
                    }

                    if (cancellationRequested && !cancellationRequestWritten)
                    {
                        cancellationRequestWritten = TryWriteCancellationRequest(
                            command.Profile,
                            logFailure: false);
                        if (cancellationRequestWritten)
                        {
                            DataTableEditorDiagnostics.Publish(
                                DataTableDiagnosticLevel.Info,
                                BuildLifecycleMessage(
                                    "Safe cancellation request persisted after the writer lock became available",
                                    command.Operation,
                                    command.Profile,
                                    processId));
                        }
                    }

                    if (cancellationRequested && stopwatch.ElapsedMilliseconds >= cancellationDeadline)
                    {
                        DataTableEditorDiagnostics.Publish(
                            DataTableDiagnosticLevel.Warning,
                            BuildLifecycleMessage(
                                "Cancellation grace period expired; forced termination requested",
                                command.Operation,
                                command.Profile,
                                processId));
                        terminationUnconfirmed |= !TryKill(process);
                        break;
                    }
                }

                MarkCompleting(runId);
                process.WaitForExit(10_000);
                terminationUnconfirmed |= !CompleteReaders(process, outputReader, errorReader);
                int exitCode = process.HasExited ? process.ExitCode : -1;
                string recoveryRunId = ReadRecoveryRunId(command.Profile.WriterLockDirectory);
                bool recoveryRequired = RequiresRecoveryAfterProcessExit(
                    exitCode,
                    terminationUnconfirmed,
                    Directory.Exists(command.Profile.WriterLockDirectory));
                bool cancelled = exitCode == 2 || cancellationRequested;
                bool success = exitCode == 0 && !cancelled && !timedOut && !recoveryRequired;
                string message = success
                    ? string.Empty
                    : recoveryRequired
                        ? "Pipeline recovery is required before another generation run."
                        : timedOut
                            ? "Pipeline timed out and was cancelled at the safest available boundary."
                             : cancelled
                                 ? "Pipeline was cancelled."
                                 : "Pipeline failed with exit code " + exitCode + ".";
                DataTableEditorDiagnostics.Publish(
                    recoveryRequired ? DataTableDiagnosticLevel.Error :
                    success ? DataTableDiagnosticLevel.Info : DataTableDiagnosticLevel.Warning,
                    BuildLifecycleMessage(
                        "dotnet process exited with code " + exitCode +
                        (recoveryRequired ? "; recovery is required" : string.Empty),
                        command.Operation,
                        command.Profile,
                        processId));
                return new DataTableLubanRunResult(
                    success,
                    cancelled,
                    timedOut,
                    recoveryRequired,
                    outputBudget.WasTruncated,
                    exitCode,
                    stopwatch.ElapsedMilliseconds,
                    output.GetText(),
                    error.GetText(),
                    recoveryRunId,
                    message);
            }
            catch (Exception exception) when (IsRecoverableRunnerException(exception))
            {
                bool terminationConfirmed = TryKill(process);
                string readerFailure = TryCompleteReadersAfterFailure(process, outputReader, errorReader);
                string recoveryRunId = ReadRecoveryRunId(command.Profile.WriterLockDirectory);
                DataTableEditorDiagnostics.PublishException(
                    DataTableDiagnosticLevel.Error,
                    exception,
                    BuildLifecycleMessage(
                        "Process execution failed",
                        command.Operation,
                        command.Profile,
                        GetProcessIdOrZero(process)));
                return new DataTableLubanRunResult(
                    false,
                    cancellationRequested,
                    timedOut,
                    terminationUnconfirmed ||
                    !terminationConfirmed ||
                    Directory.Exists(command.Profile.WriterLockDirectory),
                    outputBudget.WasTruncated,
                    -1,
                    stopwatch.ElapsedMilliseconds,
                    output.GetText(),
                    error.GetText(),
                    recoveryRunId,
                    readerFailure.Length == 0
                        ? exception.Message
                        : exception.Message + " Reader completion error: " + readerFailure);
            }
            finally
            {
                MarkCompleting(runId);
                try
                {
                    EnsureReaderThreadsStopped(process, outputReader, errorReader);
                }
                finally
                {
                    DetachActiveProcess(runId, process);
                }
            }
        }

        internal static bool TryBeginRun(DataTableLubanCommand command, out long runId)
        {
            lock (ActiveProcessSync)
            {
                if (_runReserved || _lifecycleShutdownRequested)
                {
                    runId = 0;
                    return false;
                }

                runId = NextRunIdLocked();
                _activeRunId = runId;
                _runReserved = true;
                _activeProcess = null;
                _activeProfile = command.Profile;
                _activeOperation = command.Operation;
                _cancelRequested = false;
                _activeProcessStarted = false;
                long now = DateTime.UtcNow.Ticks;
                _currentState = new DataTableLubanRunnerState(
                    NextStateRevisionLocked(),
                    DataTableLubanRunnerPhase.Preparing,
                    true,
                    command.Operation,
                    command.Profile.ProfileName,
                    command.Profile.BuildConfigurationPath,
                    0,
                    now,
                    now,
                    false,
                    default);
                return true;
            }
        }

        private static bool TryAttachActiveProcess(long runId, Process process)
        {
            lock (ActiveProcessSync)
            {
                if (!_runReserved ||
                    _activeRunId != runId ||
                    _activeProcess != null ||
                    _lifecycleShutdownRequested)
                {
                    return false;
                }

                _activeProcess = process;
                _activeProcessStarted = false;
                SetActiveStateLocked(
                    _cancelRequested
                        ? DataTableLubanRunnerPhase.CancellationRequested
                        : DataTableLubanRunnerPhase.StartingProcess,
                    0);
                return true;
            }
        }

        /// <summary>
        /// Starts the currently attached process in the same lifecycle critical section that
        /// closes during shutdown. Process start is a single cold-path operation; process-tree
        /// termination remains outside the critical section.
        /// </summary>
        internal static bool TryStartAttachedProcess(Process process)
        {
            if (process == null)
            {
                throw new ArgumentNullException(nameof(process));
            }

            lock (ActiveProcessSync)
            {
                if (!_runReserved ||
                    !ReferenceEquals(_activeProcess, process) ||
                    _lifecycleShutdownRequested ||
                    _activeProcessStarted)
                {
                    return false;
                }

                bool started = process.Start();
                _activeProcessStarted = started;
                return started;
            }
        }

        private static bool IsShutdownRequested(long runId, Process process)
        {
            lock (ActiveProcessSync)
            {
                return _runReserved &&
                       _activeRunId == runId &&
                       ReferenceEquals(_activeProcess, process) &&
                       _lifecycleShutdownRequested;
            }
        }

        private static bool IsLifecycleShutdownRequested()
        {
            lock (ActiveProcessSync)
            {
                return _lifecycleShutdownRequested;
            }
        }

        /// <summary>
        /// Focused test seam for cancellation and diagnostic reentry without starting a child process.
        /// </summary>
        internal static bool TrySetActiveProcess(
            Process process,
            DataTableLubanProfile profile,
            DataTableLubanRunnerPhase phase = DataTableLubanRunnerPhase.Running)
        {
            if (process == null)
            {
                throw new ArgumentNullException(nameof(process));
            }

            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            if (phase != DataTableLubanRunnerPhase.StartingProcess &&
                phase != DataTableLubanRunnerPhase.Running &&
                phase != DataTableLubanRunnerPhase.Completing)
            {
                throw new ArgumentOutOfRangeException(nameof(phase));
            }

            lock (ActiveProcessSync)
            {
                if (_runReserved || _lifecycleShutdownRequested)
                {
                    return false;
                }

                _activeRunId = NextRunIdLocked();
                _runReserved = true;
                _activeProcess = process;
                _activeProfile = profile;
                _activeOperation = DataTableLubanOperation.Check;
                _cancelRequested = false;
                _activeProcessStarted = phase != DataTableLubanRunnerPhase.StartingProcess;
                long now = DateTime.UtcNow.Ticks;
                _currentState = new DataTableLubanRunnerState(
                    NextStateRevisionLocked(),
                    phase,
                    true,
                    _activeOperation,
                    profile.ProfileName,
                    profile.BuildConfigurationPath,
                    0,
                    now,
                    now,
                    false,
                    default);
                return true;
            }
        }

        internal static void ClearActiveProcess(Process process)
        {
            lock (ActiveProcessSync)
            {
                if (!ReferenceEquals(_activeProcess, process))
                {
                    return;
                }

                _activeProcess = null;
                _activeProfile = null;
                _activeRunId = 0;
                _runReserved = false;
                _cancelRequested = false;
                _activeProcessStarted = false;
                _currentState = DataTableLubanRunnerState.CreateIdle(
                    NextStateRevisionLocked());
            }
        }

        private static void DetachActiveProcess(long runId, Process process)
        {
            lock (ActiveProcessSync)
            {
                if (_activeRunId != runId || !ReferenceEquals(_activeProcess, process))
                {
                    return;
                }

                _activeProcess = null;
                _activeProcessStarted = false;
                SetActiveStateLocked(DataTableLubanRunnerPhase.Completing, 0);
            }
        }

        private static void MarkProcessRunning(long runId, int processId)
        {
            lock (ActiveProcessSync)
            {
                if (_runReserved && _activeRunId == runId)
                {
                    SetActiveStateLocked(
                        _cancelRequested
                            ? DataTableLubanRunnerPhase.CancellationRequested
                            : DataTableLubanRunnerPhase.Running,
                        processId);
                }
            }
        }

        private static void MarkCancellationRequested(long runId, int processId)
        {
            lock (ActiveProcessSync)
            {
                if (_runReserved && _activeRunId == runId)
                {
                    _cancelRequested = true;
                    SetActiveStateLocked(
                        DataTableLubanRunnerPhase.CancellationRequested,
                        processId);
                }
            }
        }

        private static void MarkCompleting(long runId)
        {
            lock (ActiveProcessSync)
            {
                if (_runReserved && _activeRunId == runId)
                {
                    SetActiveStateLocked(
                        DataTableLubanRunnerPhase.Completing,
                        _currentState.ProcessId);
                }
            }
        }

        private static void CompleteRun(
            long runId,
            DataTableLubanCommand command,
            DataTableLubanRunResult result)
        {
            lock (ActiveProcessSync)
            {
                if (!_runReserved || _activeRunId != runId)
                {
                    return;
                }

                DataTableLubanRunnerPhase phase = GetTerminalPhase(result);
                long now = DateTime.UtcNow.Ticks;
                _currentState = new DataTableLubanRunnerState(
                    NextStateRevisionLocked(),
                    phase,
                    false,
                    command.Operation,
                    command.Profile.ProfileName,
                    command.Profile.BuildConfigurationPath,
                    0,
                    _currentState.StartedUtcTicks,
                    now,
                    true,
                    result);
                _activeProcess = null;
                _activeProfile = null;
                _activeRunId = 0;
                _runReserved = false;
                _cancelRequested = false;
                _activeProcessStarted = false;
            }
        }

        private static void AbandonRun(long runId, DataTableLubanCommand command)
        {
            lock (ActiveProcessSync)
            {
                if (!_runReserved || _activeRunId != runId)
                {
                    return;
                }

                long now = DateTime.UtcNow.Ticks;
                _currentState = new DataTableLubanRunnerState(
                    NextStateRevisionLocked(),
                    DataTableLubanRunnerPhase.Failed,
                    false,
                    command.Operation,
                    command.Profile.ProfileName,
                    command.Profile.BuildConfigurationPath,
                    0,
                    _currentState.StartedUtcTicks,
                    now,
                    false,
                    default);
                _activeProcess = null;
                _activeProfile = null;
                _activeRunId = 0;
                _runReserved = false;
                _cancelRequested = false;
                _activeProcessStarted = false;
            }
        }

        private static void RecordStandaloneResult(
            DataTableLubanCommand command,
            DataTableLubanRunResult result)
        {
            lock (ActiveProcessSync)
            {
                if (_runReserved)
                {
                    return;
                }

                long now = DateTime.UtcNow.Ticks;
                _currentState = new DataTableLubanRunnerState(
                    NextStateRevisionLocked(),
                    GetTerminalPhase(result),
                    false,
                    command.Operation,
                    command.Profile.ProfileName,
                    command.Profile.BuildConfigurationPath,
                    0,
                    now,
                    now,
                    true,
                    result);
            }
        }

        private static void SetActiveStateLocked(
            DataTableLubanRunnerPhase phase,
            int processId)
        {
            if (!_runReserved || _activeProfile == null)
            {
                return;
            }

            _currentState = new DataTableLubanRunnerState(
                NextStateRevisionLocked(),
                phase,
                true,
                _activeOperation,
                _activeProfile.ProfileName,
                _activeProfile.BuildConfigurationPath,
                processId,
                _currentState.StartedUtcTicks,
                DateTime.UtcNow.Ticks,
                false,
                default);
        }

        private static DataTableLubanRunnerPhase GetTerminalPhase(
            DataTableLubanRunResult result)
        {
            if (result.RecoveryRequired)
            {
                return DataTableLubanRunnerPhase.RecoveryRequired;
            }

            if (result.TimedOut)
            {
                return DataTableLubanRunnerPhase.TimedOut;
            }

            if (result.Cancelled)
            {
                return DataTableLubanRunnerPhase.Cancelled;
            }

            return result.Success
                ? DataTableLubanRunnerPhase.Succeeded
                : DataTableLubanRunnerPhase.Failed;
        }

        private static long NextRunIdLocked()
        {
            _nextRunId = unchecked(_nextRunId + 1);
            if (_nextRunId == 0)
            {
                _nextRunId = 1;
            }

            return _nextRunId;
        }

        private static long NextStateRevisionLocked()
        {
            _stateRevision = unchecked(_stateRevision + 1);
            return _stateRevision;
        }

        private static bool GetCancelRequested()
        {
            lock (ActiveProcessSync)
            {
                return _cancelRequested;
            }
        }

        internal static void RequestCancellationForShutdown()
        {
            Process process;
            DataTableLubanProfile profile;
            DataTableLubanOperation operation;
            bool processStarted;
            lock (ActiveProcessSync)
            {
                _lifecycleShutdownRequested = true;
                if (!_runReserved)
                {
                    return;
                }

                process = _activeProcess;
                profile = _activeProfile;
                operation = _activeOperation;
                processStarted = _activeProcessStarted;
            }

            if (process == null || !processStarted)
            {
                DataTableEditorDiagnostics.Publish(
                    DataTableDiagnosticLevel.Warning,
                    BuildLifecycleMessage(
                        "Editor shutdown or assembly reload prevented the reserved process from starting",
                        operation,
                        profile,
                        0));
                return;
            }

            int processId = GetProcessIdOrZero(process);
            bool confirmed = TryTerminateProcessTree(
                process,
                2_000,
                out string errorMessage);
            DataTableEditorDiagnostics.Publish(
                confirmed ? DataTableDiagnosticLevel.Warning : DataTableDiagnosticLevel.Error,
                BuildLifecycleMessage(
                    confirmed
                        ? "Editor shutdown or assembly reload terminated the active process tree"
                        : "Editor shutdown or assembly reload could not confirm active process-tree termination: " +
                          errorMessage,
                    operation,
                    profile,
                    processId));
        }

        /// <summary>
        /// Test isolation seam. Production lifecycle completion must never reopen this gate.
        /// </summary>
        internal static void ResetLifecycleShutdownForTests()
        {
            lock (ActiveProcessSync)
            {
                if (_runReserved)
                {
                    throw new InvalidOperationException(
                        "The lifecycle shutdown gate cannot be reset while a pipeline run is reserved.");
                }

                _lifecycleShutdownRequested = false;
            }
        }

        private static bool TryWriteCancellationRequest(
            DataTableLubanProfile profile,
            bool logFailure = true)
        {
            if (profile == null ||
                !TryValidateWriterLockDirectory(profile.WriterLockDirectory, logFailure))
            {
                return false;
            }

            try
            {
                string requestPath = Path.Combine(profile.WriterLockDirectory, "cancel.request");
                if (File.Exists(requestPath))
                {
                    if ((File.GetAttributes(requestPath) & FileAttributes.ReparsePoint) != 0)
                    {
                        if (logFailure)
                        {
                            DataTableEditorDiagnostics.Publish(
                                DataTableDiagnosticLevel.Warning,
                                "The pipeline cancellation request path is a reparse point and was rejected.");
                        }

                        return false;
                    }

                    return true;
                }

                using var stream = new FileStream(
                    requestPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.Read);
                byte[] bytes = new UTF8Encoding(false).GetBytes("cancel\n");
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(flushToDisk: true);
                return true;
            }
            catch (IOException) when (File.Exists(Path.Combine(profile.WriterLockDirectory, "cancel.request")))
            {
                // Another cancellation request won the exclusive create race.
                return true;
            }
            catch (Exception exception) when (IsRecoverableRunnerException(exception))
            {
                if (logFailure)
                {
                    DataTableEditorDiagnostics.PublishException(
                        DataTableDiagnosticLevel.Warning,
                        exception,
                        "Could not write the pipeline cancellation request.");
                }

                return false;
            }
        }

        private static string ReadRecoveryRunId(string lockDirectory)
        {
            try
            {
                if (!TryValidateWriterLockDirectory(lockDirectory, logFailure: true))
                {
                    return string.Empty;
                }

                string ownerPath = Path.Combine(lockDirectory, "owner.txt");
                if (!File.Exists(ownerPath))
                {
                    return string.Empty;
                }

                const long maximumOwnerBytes = 64 * 1024;
                var ownerFile = new FileInfo(ownerPath);
                if ((ownerFile.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    DataTableEditorDiagnostics.Publish(
                        DataTableDiagnosticLevel.Warning,
                        "The retained DataTable writer-lock owner file is a reparse point and was rejected.");
                    return string.Empty;
                }

                if (ownerFile.Length > maximumOwnerBytes)
                {
                    DataTableEditorDiagnostics.Publish(
                        DataTableDiagnosticLevel.Warning,
                        "The retained DataTable writer-lock owner file exceeds the 64 KiB read limit.");
                    return string.Empty;
                }

                using var stream = new FileStream(
                    ownerPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    4096,
                    FileOptions.SequentialScan);
                using var reader = new StreamReader(
                    stream,
                    new UTF8Encoding(false, true),
                    detectEncodingFromByteOrderMarks: true,
                    bufferSize: 4096,
                    leaveOpen: false);
                for (int lineNumber = 0; lineNumber < 64; lineNumber++)
                {
                    string line = reader.ReadLine();
                    if (line == null)
                    {
                        break;
                    }

                    if (line.StartsWith("run_id=", StringComparison.Ordinal))
                    {
                        string runId = line.Substring("run_id=".Length);
                        if (IsValidRecoveryRunId(runId))
                        {
                            return runId;
                        }

                        DataTableEditorDiagnostics.Publish(
                            DataTableDiagnosticLevel.Warning,
                            "The retained DataTable writer-lock owner contains an invalid recovery run ID.");
                        return string.Empty;
                    }
                }
            }
            catch (Exception exception) when (IsRecoverableRunnerException(exception))
            {
                DataTableEditorDiagnostics.PublishException(
                    DataTableDiagnosticLevel.Warning,
                    exception,
                    "Recovery is required, but the retained writer-lock owner could not be read.");
            }

            return string.Empty;
        }

        private static bool TryValidateWriterLockDirectory(
            string lockDirectory,
            bool logFailure)
        {
            if (string.IsNullOrEmpty(lockDirectory) || !Directory.Exists(lockDirectory))
            {
                return false;
            }

            try
            {
                if ((File.GetAttributes(lockDirectory) & FileAttributes.ReparsePoint) == 0)
                {
                    return true;
                }

                if (logFailure)
                {
                    DataTableEditorDiagnostics.Publish(
                        DataTableDiagnosticLevel.Warning,
                        "The DataTable writer-lock directory is a reparse point and was rejected.");
                }
            }
            catch (Exception exception) when (IsRecoverableRunnerException(exception))
            {
                if (logFailure)
                {
                    DataTableEditorDiagnostics.PublishException(
                        DataTableDiagnosticLevel.Warning,
                        exception,
                        "The DataTable writer-lock directory could not be validated.");
                }
            }

            return false;
        }

        private static bool IsValidRecoveryRunId(string runId)
        {
            if (string.IsNullOrEmpty(runId) || runId.Length != 32)
            {
                return false;
            }

            for (int index = 0; index < runId.Length; index++)
            {
                if (!Uri.IsHexDigit(runId[index]))
                {
                    return false;
                }
            }

            return true;
        }

        private static ProcessOutputReader StartReader(StreamReader reader, BoundedTextBuffer destination)
        {
            var state = new ProcessOutputReader(reader, destination);
            state.Start();
            return state;
        }

        private static bool CompleteReaders(
            Process process,
            ProcessOutputReader outputReader,
            ProcessOutputReader errorReader)
        {
            if (ReadersCompleted(outputReader, errorReader, 10_000))
            {
                outputReader?.ThrowIfFailed();
                errorReader?.ThrowIfFailed();
                return true;
            }

            bool terminationConfirmed = TryKill(process);
            CloseRedirectedReaders(process);
            if (!ReadersCompleted(outputReader, errorReader, 10_000))
            {
                throw new InvalidOperationException(
                    "Timed out while terminating DataTable pipeline output readers.");
            }

            outputReader?.ThrowIfFailed();
            errorReader?.ThrowIfFailed();
            return terminationConfirmed;
        }

        private static string TryCompleteReadersAfterFailure(
            Process process,
            ProcessOutputReader outputReader,
            ProcessOutputReader errorReader)
        {
            try
            {
                CompleteReaders(process, outputReader, errorReader);
                return string.Empty;
            }
            catch (Exception exception) when (IsRecoverableRunnerException(exception))
            {
                return exception.Message;
            }
        }

        private static bool ReadersCompleted(
            ProcessOutputReader outputReader,
            ProcessOutputReader errorReader,
            int timeoutMilliseconds)
        {
            long deadline = Stopwatch.GetTimestamp() +
                            (long)timeoutMilliseconds * Stopwatch.Frequency / 1000;
            return JoinBeforeDeadline(outputReader, deadline) && JoinBeforeDeadline(errorReader, deadline);
        }

        private static bool JoinBeforeDeadline(ProcessOutputReader reader, long deadline)
        {
            if (reader == null)
            {
                return true;
            }

            long remainingTicks = deadline - Stopwatch.GetTimestamp();
            int remainingMilliseconds = remainingTicks <= 0
                ? 0
                : (int)Math.Min(int.MaxValue, remainingTicks * 1000 / Stopwatch.Frequency);
            return reader.Join(remainingMilliseconds);
        }

        private static void CloseRedirectedReaders(Process process)
        {
            try
            {
                process.StandardOutput.Close();
                process.StandardError.Close();
            }
            catch (Exception exception) when (IsRecoverableRunnerException(exception))
            {
                // Reader failures are observed by ProcessOutputReader and reported by the caller.
            }
        }

        private static void EnsureReaderThreadsStopped(
            Process process,
            ProcessOutputReader outputReader,
            ProcessOutputReader errorReader)
        {
            if ((outputReader == null || !outputReader.IsAlive) &&
                (errorReader == null || !errorReader.IsAlive))
            {
                return;
            }

            TryKill(process);
            CloseRedirectedReaders(process);
            outputReader?.Interrupt();
            errorReader?.Interrupt();
            if (!ReadersCompleted(outputReader, errorReader, 10_000))
            {
                throw new InvalidOperationException(
                    "DataTable pipeline output readers could not be terminated before process disposal.");
            }
        }

        private static bool TryKill(Process process)
        {
            bool confirmed = TryTerminateProcessTree(
                process,
                10_000,
                out string errorMessage);
            if (!confirmed &&
                !string.IsNullOrEmpty(errorMessage))
            {
                DataTableEditorDiagnostics.Publish(
                    DataTableDiagnosticLevel.Warning,
                    errorMessage);
            }

            return confirmed;
        }

        internal static bool RequiresRecoveryAfterProcessExit(
            int exitCode,
            bool terminationUnconfirmed,
            bool writerLockExists)
        {
            return terminationUnconfirmed ||
                   exitCode == 3 ||
                   (exitCode != 0 && writerLockExists);
        }

        /// <summary>
        /// Requests descendant-aware termination. A false result means the direct owner may have
        /// stopped, but descendant termination was not confirmed and recovery evidence must remain.
        /// </summary>
        internal static bool TryTerminateProcessTree(
            Process process,
            int waitMilliseconds,
            out string errorMessage)
        {
            if (process == null)
            {
                throw new ArgumentNullException(nameof(process));
            }

            if (waitMilliseconds < 0 || waitMilliseconds > 60_000)
            {
                throw new ArgumentOutOfRangeException(nameof(waitMilliseconds));
            }

            errorMessage = string.Empty;
            int processId;
            try
            {
                if (process.HasExited)
                {
                    errorMessage =
                        "The directly owned process had already exited, so descendant termination could not be confirmed.";
                    return false;
                }

                processId = process.Id;
            }
            catch (ObjectDisposedException)
            {
                errorMessage =
                    "The process handle was already disposed, so process-tree termination could not be confirmed.";
                return false;
            }
            catch (InvalidOperationException)
            {
                // Process.Start did not complete, so there is no child tree to terminate.
                return true;
            }

            string treeTerminationError = string.Empty;
            MethodInfo killTreeMethod = typeof(Process).GetMethod(
                "Kill",
                BindingFlags.Instance | BindingFlags.Public,
                binder: null,
                types: new[] { typeof(bool) },
                modifiers: null);
            if (killTreeMethod != null)
            {
                try
                {
                    killTreeMethod.Invoke(process, new object[] { true });
                    if (TryWaitForExit(process, waitMilliseconds))
                    {
                        return true;
                    }

                    treeTerminationError = "Runtime process-tree termination did not confirm exit.";
                }
                catch (Exception exception) when (
                    IsRecoverableRunnerException(exception.GetBaseException()))
                {
                    treeTerminationError = exception.GetBaseException().Message;
                }
            }

            if (Path.DirectorySeparatorChar == '\\')
            {
                try
                {
                    if (process.HasExited)
                    {
                        treeTerminationError =
                            "The directly owned process exited before Windows tree termination could be requested.";
                    }

                    string systemDirectory = Environment.GetFolderPath(
                        Environment.SpecialFolder.System);
                    string taskKillPath = Path.Combine(systemDirectory, "taskkill.exe");
                    if (!process.HasExited &&
                        !string.IsNullOrEmpty(systemDirectory) &&
                        Path.IsPathRooted(taskKillPath) &&
                        File.Exists(taskKillPath))
                    {
                        using var taskKill = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = taskKillPath,
                                Arguments = "/PID " + processId + " /T /F",
                                WorkingDirectory = systemDirectory,
                                UseShellExecute = false,
                                CreateNoWindow = true,
                            },
                        };
                        if (taskKill.Start() &&
                            taskKill.WaitForExit(waitMilliseconds) &&
                            taskKill.ExitCode == 0 &&
                            TryWaitForExit(process, waitMilliseconds))
                        {
                            return true;
                        }

                        treeTerminationError = "Windows taskkill did not confirm process-tree exit.";
                    }
                }
                catch (Exception exception) when (IsRecoverableRunnerException(exception))
                {
                    treeTerminationError = exception.Message;
                }
            }

            bool directOwnerExited = false;
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }

                directOwnerExited = TryWaitForExit(process, waitMilliseconds);
            }
            catch (Exception exception) when (IsRecoverableRunnerException(exception))
            {
                errorMessage = "Process-tree termination failed: " + exception.Message;
                return false;
            }

            errorMessage = directOwnerExited
                ? "The directly owned process exited, but descendant termination could not be confirmed. "
                : "The directly owned process and its descendants could not be confirmed stopped. ";
            errorMessage +=
                "Retain and inspect the DataTable writer lock before another write." +
                (treeTerminationError.Length == 0
                    ? string.Empty
                    : " Tree termination detail: " + treeTerminationError);
            return false;
        }

        private static bool TryWaitForExit(Process process, int waitMilliseconds)
        {
            try
            {
                return process.HasExited || process.WaitForExit(waitMilliseconds);
            }
            catch (Exception exception) when (IsRecoverableRunnerException(exception))
            {
                return false;
            }
        }

        internal static bool IsRecoverableRunnerException(Exception exception)
        {
            return exception is not OutOfMemoryException and
                   not AccessViolationException and
                   not AppDomainUnloadedException and
                   not BadImageFormatException and
                   not CannotUnloadAppDomainException and
                   not StackOverflowException and
                   not ThreadAbortException;
        }

        private static DataTableLubanRunResult CreateCancelledBeforeStart()
        {
            return new DataTableLubanRunResult(
                false, true, false, false, false, -1, 0,
                string.Empty, string.Empty, string.Empty,
                "Pipeline was cancelled before process start.");
        }

        private static DataTableLubanRunResult CreateFailure(string message)
        {
            return new DataTableLubanRunResult(
                false, false, false, false, false, -1, 0,
                string.Empty, string.Empty, string.Empty, message);
        }

        private static DataTableLubanRunResult CreateEditorFailure(
            DataTableLubanRunResult processResult,
            string message)
        {
            string combinedMessage = string.IsNullOrEmpty(processResult.ErrorMessage)
                ? message
                : processResult.ErrorMessage + " " + message;
            return new DataTableLubanRunResult(
                false,
                processResult.Cancelled,
                processResult.TimedOut,
                processResult.RecoveryRequired,
                processResult.OutputTruncated,
                processResult.ExitCode,
                processResult.DurationMilliseconds,
                processResult.StandardOutput,
                processResult.StandardError,
                processResult.RecoveryRunId,
                combinedMessage);
        }

        private static void LogResult(
            DataTableLubanCommand command,
            DataTableLubanRunResult result)
        {
            var builder = new StringBuilder(1024);
            builder.Append("DataTable Luban operation completed. Operation=")
                .Append(command.Operation)
                .Append("; Profile=")
                .Append(command.Profile.ProfileName)
                .Append("; Config=");
            AppendDiagnosticValue(builder, command.Profile.BuildConfigurationPath);
            builder.Append("; Outcome=")
                .Append(GetTerminalPhase(result))
                .Append("; ExitCode=")
                .Append(result.ExitCode)
                .Append("; DurationMilliseconds=")
                .Append(result.DurationMilliseconds)
                .Append(';');
            if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                builder.AppendLine().Append(result.ErrorMessage);
            }

            if (result.RecoveryRequired && !string.IsNullOrEmpty(result.RecoveryRunId))
            {
                builder.AppendLine().Append("Recovery run ID: ").Append(result.RecoveryRunId);
            }

            if (!result.Success)
            {
                AppendOutput(builder, "stdout", result.StandardOutput);
                AppendOutput(builder, "stderr", result.StandardError);
                DataTableEditorDiagnostics.Publish(
                    result.RecoveryRequired
                        ? DataTableDiagnosticLevel.Error
                        : DataTableDiagnosticLevel.Warning,
                    builder.ToString());
            }
            else
            {
                DataTableEditorDiagnostics.Publish(
                    DataTableDiagnosticLevel.Info,
                    builder.ToString());
            }
        }

        private static string BuildLifecycleMessage(
            string action,
            DataTableLubanOperation operation,
            DataTableLubanProfile profile,
            int processId)
        {
            var builder = new StringBuilder(384);
            builder.Append("DataTable Luban lifecycle: ");
            AppendDiagnosticValue(builder, action);
            builder.Append(". Operation=")
                .Append(operation)
                .Append("; Profile=")
                .Append(profile?.ProfileName ?? string.Empty)
                .Append("; Config=");
            AppendDiagnosticValue(
                builder,
                profile?.BuildConfigurationPath ?? string.Empty);
            if (processId > 0)
            {
                builder.Append("; ProcessId=").Append(processId);
            }

            builder.Append(';');
            return builder.ToString();
        }

        private static void AppendDiagnosticValue(StringBuilder builder, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                builder.Append("<none>");
                return;
            }

            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                builder.Append(character == '\r' || character == '\n' ? ' ' : character);
            }
        }

        private static int GetProcessIdOrZero(Process process)
        {
            try
            {
                return process?.Id ?? 0;
            }
            catch (Exception exception) when (IsRecoverableRunnerException(exception))
            {
                return 0;
            }
        }

        private static void AppendOutput(StringBuilder builder, string label, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                builder.AppendLine().Append(label).Append(':').AppendLine().Append(value.TrimEnd());
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

        internal sealed class CapturedOutputBudget
        {
            private readonly object _sync = new object();
            private readonly int _maximumCharacters;
            private int _acceptedCharacters;
            private bool _truncated;

            internal CapturedOutputBudget(int maximumCharacters)
            {
                if (maximumCharacters <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(maximumCharacters));
                }

                _maximumCharacters = maximumCharacters;
            }

            internal int AcceptedCharacters
            {
                get
                {
                    lock (_sync)
                    {
                        return _acceptedCharacters;
                    }
                }
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

            internal int Claim(int requestedCharacters)
            {
                if (requestedCharacters < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(requestedCharacters));
                }

                lock (_sync)
                {
                    int accepted = Math.Min(
                        requestedCharacters,
                        _maximumCharacters - _acceptedCharacters);
                    _acceptedCharacters += accepted;
                    _truncated |= accepted != requestedCharacters;
                    return accepted;
                }
            }
        }

        internal sealed class BoundedTextBuffer
        {
            private readonly object _sync = new object();
            private readonly StringBuilder _builder;
            private readonly CapturedOutputBudget _budget;

            internal BoundedTextBuffer(CapturedOutputBudget budget)
            {
                _budget = budget ?? throw new ArgumentNullException(nameof(budget));
                _builder = new StringBuilder(4096);
            }

            internal int Length
            {
                get
                {
                    lock (_sync)
                    {
                        return _builder.Length;
                    }
                }
            }

            internal void Append(char[] buffer, int count)
            {
                int accepted = _budget.Claim(count);
                if (accepted == 0)
                {
                    return;
                }

                lock (_sync)
                {
                    _builder.Append(buffer, 0, accepted);
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

        private sealed class ProcessOutputReader
        {
            private readonly StreamReader _reader;
            private readonly BoundedTextBuffer _destination;
            private readonly Thread _thread;
            private Exception _failure;

            internal ProcessOutputReader(StreamReader reader, BoundedTextBuffer destination)
            {
                _reader = reader ?? throw new ArgumentNullException(nameof(reader));
                _destination = destination ?? throw new ArgumentNullException(nameof(destination));
                _thread = new Thread(Read)
                {
                    IsBackground = true,
                    Name = "CycloneGames.DataTable.Pipeline.Output",
                };
            }

            internal void Start()
            {
                _thread.Start();
            }

            internal bool Join(int timeoutMilliseconds)
            {
                return _thread.Join(timeoutMilliseconds);
            }

            internal bool IsAlive => _thread.IsAlive;

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
                    ExceptionDispatchInfo.Capture(_failure).Throw();
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
                        _destination.Append(buffer, count);
                    }
                }
                catch (Exception exception)
                {
                    _failure = exception;
                }
            }
        }
    }
}
