using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace CycloneGames.DataTable.Unity.Editor
{
    /// <summary>
    /// Input contract for a Luban generation process.
    /// External projects can create this directly instead of modifying the DataTable package.
    /// </summary>
    public class DataTableLubanRunRequest
    {
        public DataTableLubanSettings Settings { get; set; }
        public string ProjectRoot { get; set; }
        public string ProjectDirectory { get; set; }
        public string ScriptName { get; set; }
        public string ScriptExtension { get; set; }
        public string ScriptArguments { get; set; }
        public string WorkingDirectory { get; set; }
        public string ScriptPath { get; set; }
        public string ConfigAssetPath { get; set; }
        public string RequestCreationError { get; set; }
        public int TimeoutMilliseconds { get; set; }
        public bool AutoRefreshAssets { get; set; }
        public bool LogOutputToUnity { get; set; } = true;

        /// <summary>
        /// Maximum retained character count for stdout and stderr combined.
        /// Process output beyond this budget is discarded and reported as truncated.
        /// </summary>
        public int MaxCapturedOutputCharacters { get; set; } = 1024 * 1024;

        /// <summary>
        /// Whether process output should be included in the final Unity Console message.
        /// Output is never logged from background process callbacks.
        /// </summary>
        public bool StreamOutputToUnity { get; set; }
    }

    /// <summary>
    /// Structured result for menu, CI, and custom project tooling.
    /// </summary>
    public readonly struct DataTableLubanRunResult
    {
        public DataTableLubanRunResult(
            bool success,
            bool timedOut,
            int exitCode,
            string scriptPath,
            string workingDirectory,
            string standardOutput,
            string standardError,
            long durationMilliseconds,
            string errorMessage)
            : this(
                success,
                timedOut,
                false,
                false,
                exitCode,
                scriptPath,
                workingDirectory,
                standardOutput,
                standardError,
                durationMilliseconds,
                errorMessage)
        {
        }

        public DataTableLubanRunResult(
            bool success,
            bool timedOut,
            bool cancelled,
            bool outputTruncated,
            int exitCode,
            string scriptPath,
            string workingDirectory,
            string standardOutput,
            string standardError,
            long durationMilliseconds,
            string errorMessage)
        {
            Success = success;
            TimedOut = timedOut;
            Cancelled = cancelled;
            OutputTruncated = outputTruncated;
            ExitCode = exitCode;
            ScriptPath = scriptPath;
            WorkingDirectory = workingDirectory;
            StandardOutput = standardOutput;
            StandardError = standardError;
            DurationMilliseconds = durationMilliseconds;
            ErrorMessage = errorMessage;
        }

        public bool Success { get; }
        public bool TimedOut { get; }
        public bool Cancelled { get; }
        public bool OutputTruncated { get; }
        public int ExitCode { get; }
        public string ScriptPath { get; }
        public string WorkingDirectory { get; }
        public string StandardOutput { get; }
        public string StandardError { get; }
        public long DurationMilliseconds { get; }
        public string ErrorMessage { get; }
    }

    /// <summary>
    /// Menu item and programmatic entry point for running the Luban code-generation script.
    /// </summary>
    [InitializeOnLoad]
    public static class DataTableLubanRunner
    {
        private const string MenuPath = "Tools/CycloneGames/DataTable/Run Luban Build";
        private const string NamespaceCaseConflictMarker = "\u547D\u540D\u7A7A\u95F4\u5C0F\u5199\u91CD\u590D";
        private const string NamespaceCaseConflictMojibakeMarker = "\u935B\u85C9\u6095\u7ECC\u6D2A\u68FF\u704F\u5FD3\u5553";
        private const string WindowsPlatformMojibakeMarker = "\u9366\u2573\u0069\u006E\u9A9E\u51B2\u5F74";
        private const int MAX_UNITY_LOG_CHARACTERS = 60000;
        private const int MIN_CAPTURED_OUTPUT_CHARACTERS = 4096;
        private const int MAX_CAPTURED_OUTPUT_CHARACTERS = 4 * 1024 * 1024;
        private const int PROCESS_TERMINATION_GRACE_MILLISECONDS = 5000;

        private static readonly object ActiveProcessSync = new object();
        private static Process _activeProcess;
        private static bool _cancelRequested;

        private static readonly Regex LubanTypeRegex = new Regex(
            "type:'([^']+)'",
            RegexOptions.CultureInvariant);

        static DataTableLubanRunner()
        {
            AssemblyReloadEvents.beforeAssemblyReload += CancelForEditorShutdown;
            EditorApplication.quitting += CancelForEditorShutdown;
        }

        /// <summary>
        /// True while this editor process owns a running Luban process.
        /// </summary>
        public static bool IsRunning
        {
            get
            {
                lock (ActiveProcessSync)
                {
                    return _activeProcess != null;
                }
            }
        }

        /// <summary>
        /// Best-effort cancellation for an externally owned run or editor shutdown.
        /// The default synchronous main-thread entry point blocks Inspector interaction,
        /// so this is not an interactive cancellation guarantee.
        /// </summary>
        public static bool CancelActiveRun()
        {
            Process process;
            lock (ActiveProcessSync)
            {
                process = _activeProcess;
                if (process == null)
                {
                    return false;
                }

                _cancelRequested = true;
            }

            TryKill(process, out _);
            return true;
        }

        /// <summary>
        /// Optional CI/editor-script override for the Luban project directory.
        /// </summary>
        public static string LubanProjectDirOverride { get; set; }

        /// <summary>
        /// Optional CI/editor-script override for the Luban script name.
        /// </summary>
        public static string LubanScriptNameOverride { get; set; }

        public static string EffectiveLubanProjectDir =>
            LubanProjectDirOverride ?? DataTableLubanSettings.GetOrCreate().GetLubanProjectDir();

        public static string EffectiveLubanScriptName =>
            LubanScriptNameOverride ?? DataTableLubanSettings.GetOrCreate().GetLubanScriptName();

        [MenuItem(MenuPath)]
        public static void Run()
        {
            RunWithResult();
        }

        /// <summary>
        /// Run the default DataTableLubanSettings and return a structured result.
        /// </summary>
        public static DataTableLubanRunResult RunWithResult()
        {
            return RunWithResult(DataTableLubanSettings.GetOrCreate());
        }

        /// <summary>
        /// Run a derived settings type without requiring package modification.
        /// </summary>
        public static DataTableLubanRunResult RunWithResult<TSettings>()
            where TSettings : DataTableLubanSettings
        {
            return RunWithResult(DataTableLubanSettings.GetOrCreate<TSettings>());
        }

        /// <summary>
        /// Run a settings instance. Derived settings can override CreateLubanRunRequest.
        /// </summary>
        public static DataTableLubanRunResult RunWithResult(DataTableLubanSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            return RunWithResult(settings.CreateLubanRunRequest());
        }

        /// <summary>
        /// Run a fully custom request. This is the most flexible extension point for external projects.
        /// </summary>
        public static DataTableLubanRunResult RunWithResult(DataTableLubanRunRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            var validationError = ValidateRequest(request);
            if (!string.IsNullOrEmpty(validationError))
            {
                var validationResult = new DataTableLubanRunResult(
                    false,
                    false,
                    -1,
                    request.ScriptPath,
                    request.WorkingDirectory,
                    string.Empty,
                    string.Empty,
                    0,
                    validationError);
                if (request.LogOutputToUnity)
                {
                    LogResult(validationResult, true);
                }

                return validationResult;
            }

            var result = RunProcess(request);
            if (request.LogOutputToUnity)
            {
                LogResult(result, request.StreamOutputToUnity || !result.Success);
            }

            return result;
        }

        /// <summary>
        /// Build a request from settings. Custom projects can call this and then modify selected fields.
        /// </summary>
        public static DataTableLubanRunRequest CreateRequest(DataTableLubanSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            var projectRoot = string.Empty;
            var projectDir = string.Empty;
            var scriptName = string.Empty;
            var scriptExtension = string.Empty;
            var scriptArguments = string.Empty;
            var workingDirectory = string.Empty;
            var scriptPath = string.Empty;
            var timeoutMilliseconds = DataTableLubanSettings.DefaultLubanTimeoutSeconds * 1000;
            var autoRefreshAssets = false;
            string requestCreationError = null;

            try
            {
                projectRoot = settings.GetProjectRoot() ?? string.Empty;
                projectDir = LubanProjectDirOverride ?? settings.GetLubanProjectDir() ?? string.Empty;
                scriptName = LubanScriptNameOverride ?? settings.GetLubanScriptName() ?? string.Empty;
                scriptExtension = settings.GetLubanScriptExtension() ?? string.Empty;
                scriptArguments = settings.GetLubanScriptArguments() ?? string.Empty;
                workingDirectory = string.IsNullOrWhiteSpace(projectRoot)
                    ? string.Empty
                    : Path.GetFullPath(string.IsNullOrWhiteSpace(projectDir)
                        ? projectRoot
                        : Path.Combine(projectRoot, projectDir));
                scriptPath = string.IsNullOrWhiteSpace(workingDirectory) || string.IsNullOrWhiteSpace(scriptName)
                    ? string.Empty
                    : Path.Combine(workingDirectory, scriptName + scriptExtension);
                timeoutMilliseconds = settings.GetLubanTimeoutMilliseconds();
                autoRefreshAssets = settings.ShouldRefreshAssetsAfterLubanBuild();
            }
            catch (Exception exception) when (IsRecoverableException(exception))
            {
                workingDirectory = string.Empty;
                scriptPath = string.Empty;
                requestCreationError =
                    "[DataTable] Failed to resolve the serialized Luban settings: " + exception.Message;
            }

            return new DataTableLubanRunRequest
            {
                Settings = settings,
                ProjectRoot = projectRoot,
                ProjectDirectory = projectDir,
                ScriptName = scriptName,
                ScriptExtension = scriptExtension,
                ScriptArguments = scriptArguments,
                WorkingDirectory = workingDirectory,
                ScriptPath = scriptPath,
                ConfigAssetPath = AssetDatabase.GetAssetPath(settings),
                RequestCreationError = requestCreationError,
                TimeoutMilliseconds = timeoutMilliseconds,
                AutoRefreshAssets = autoRefreshAssets,
                MaxCapturedOutputCharacters = 1024 * 1024
            };
        }

        public static string ValidateRequest(DataTableLubanRunRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            if (!string.IsNullOrEmpty(request.RequestCreationError))
            {
                return request.RequestCreationError;
            }

            if (string.IsNullOrWhiteSpace(request.WorkingDirectory))
            {
                return "[DataTable] Luban working directory is empty.";
            }

            if (string.IsNullOrWhiteSpace(request.ScriptName))
            {
                return "[DataTable] Luban script name is empty.";
            }

            if (request.ScriptName == "." ||
                request.ScriptName == ".." ||
                request.ScriptName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                request.ScriptName.IndexOf(Path.DirectorySeparatorChar) >= 0 ||
                request.ScriptName.IndexOf(Path.AltDirectorySeparatorChar) >= 0)
            {
                return $"[DataTable] Luban script name contains invalid file name characters: {request.ScriptName}";
            }

            if (request.TimeoutMilliseconds <= 0)
            {
                return "[DataTable] Luban timeout must be greater than zero milliseconds.";
            }

            var expectedExtension = Application.platform == RuntimePlatform.WindowsEditor ? ".bat" : ".sh";
            if (!string.Equals(request.ScriptExtension, expectedExtension, StringComparison.OrdinalIgnoreCase))
            {
                return
                    $"[DataTable] Luban script extension '{request.ScriptExtension}' is not supported by this editor platform. " +
                    $"Expected '{expectedExtension}'.";
            }

            if (request.MaxCapturedOutputCharacters < MIN_CAPTURED_OUTPUT_CHARACTERS ||
                request.MaxCapturedOutputCharacters > MAX_CAPTURED_OUTPUT_CHARACTERS)
            {
                return
                    $"[DataTable] Luban output capture budget must be between {MIN_CAPTURED_OUTPUT_CHARACTERS} " +
                    $"and {MAX_CAPTURED_OUTPUT_CHARACTERS} characters.";
            }

            var argumentError = ValidateScriptArguments(request.ScriptArguments);
            if (!string.IsNullOrEmpty(argumentError))
            {
                return argumentError;
            }

            string fullWorkingDirectory;
            string fullScriptPath;
            string expectedScriptPath;
            try
            {
                fullWorkingDirectory = Path.GetFullPath(request.WorkingDirectory);
                fullScriptPath = Path.GetFullPath(request.ScriptPath ?? string.Empty);
                expectedScriptPath = Path.GetFullPath(Path.Combine(
                    fullWorkingDirectory,
                    request.ScriptName + (request.ScriptExtension ?? string.Empty)));
            }
            catch (Exception exception) when (IsRecoverableException(exception))
            {
                return "[DataTable] Luban path resolution failed: " + exception.Message;
            }

            if (!string.Equals(fullScriptPath, expectedScriptPath, GetPathComparison()) ||
                !IsStrictChildPath(fullWorkingDirectory, fullScriptPath))
            {
                return
                    "[DataTable] Luban script must be the configured direct child of the working directory:\n" +
                    $"  Expected: {expectedScriptPath}\n" +
                    $"  Actual  : {fullScriptPath}";
            }

            if (Application.platform == RuntimePlatform.WindowsEditor &&
                fullScriptPath.IndexOfAny(new[] { '%', '&', '|', '<', '>', '^', '!', '\r', '\n' }) >= 0)
            {
                return "[DataTable] Luban script path contains characters that are unsafe for cmd.exe: " + fullScriptPath;
            }

            if (!Directory.Exists(fullWorkingDirectory))
            {
                return $"[DataTable] Luban working directory not found: {fullWorkingDirectory}";
            }

            if (!File.Exists(fullScriptPath))
            {
                return
                    "[DataTable] Luban build script not found:\n" +
                    $"  Script path : {request.ScriptPath}\n" +
                    $"  Config asset: {(string.IsNullOrEmpty(request.ConfigAssetPath) ? "(not found)" : request.ConfigAssetPath)}";
            }

            return null;
        }

        public static string BuildFailureDialogMessage(DataTableLubanRunResult result)
        {
            if (result.Success)
            {
                return string.Empty;
            }

            var builder = new StringBuilder(512);
            builder.AppendLine(string.IsNullOrEmpty(result.ErrorMessage)
                ? "[DataTable] Luban build failed."
                : result.ErrorMessage);

            var diagnostics = BuildDiagnosticsMessage(result);
            if (!string.IsNullOrWhiteSpace(diagnostics))
            {
                builder.AppendLine();
                builder.Append(diagnostics.TrimEnd());
            }

            return builder.ToString().TrimEnd();
        }

        private static DataTableLubanRunResult RunProcess(DataTableLubanRunRequest request)
        {
            var outputBudget = Math.Max(MIN_CAPTURED_OUTPUT_CHARACTERS, request.MaxCapturedOutputCharacters);
            var output = new BoundedTextBuffer(outputBudget / 2);
            var error = new BoundedTextBuffer(outputBudget - outputBudget / 2);
            var stopwatch = Stopwatch.StartNew();

            using (var process = new Process())
            {
                process.StartInfo = CreateStartInfo(request);
                Thread outputReaderThread = null;
                Thread errorReaderThread = null;
                bool processStarted = false;
                bool processTerminationConfirmed = false;

                if (!TrySetActiveProcess(process))
                {
                    stopwatch.Stop();
                    return CreateProcessResult(
                        request,
                        output,
                        error,
                        stopwatch.ElapsedMilliseconds,
                        false,
                        false,
                        -1,
                        "[DataTable] A Luban build is already running, or a previous process could not be confirmed terminated. " +
                        "Do not start another writer. Confirm that all Luban processes stopped and audit the workspace lock/output; " +
                        "restart the Editor only after recovery.");
                }

                try
                {
                    processStarted = process.Start();
                    if (!processStarted)
                    {
                        throw new InvalidOperationException("The operating system did not start the Luban process.");
                    }

                    outputReaderThread = StartReaderThread(process.StandardOutput, output, "DataTable Luban stdout");
                    errorReaderThread = StartReaderThread(process.StandardError, error, "DataTable Luban stderr");

                    // Cancellation may race the narrow window between ownership publication and
                    // Process.Start(). Re-check after the process exists so the request cannot be
                    // lost merely because Kill was attempted on an unstarted Process instance.
                    if (WasCancellationRequested(process))
                    {
                        TryKill(process, out _);
                        processTerminationConfirmed = TryWaitForTermination(process);
                        JoinReaderThreads(outputReaderThread, errorReaderThread);
                        stopwatch.Stop();
                        return CreateProcessResult(
                            request,
                            output,
                            error,
                            stopwatch.ElapsedMilliseconds,
                            false,
                            true,
                            TryGetExitCode(process),
                            processTerminationConfirmed
                                ? "[DataTable] Luban build was cancelled."
                                : "[DataTable] Luban cancellation was requested, but process termination could not be confirmed. " +
                                  "The single-writer gate remains blocked.");
                    }

                    var completed = process.WaitForExit(request.TimeoutMilliseconds);

                    if (!completed)
                    {
                        TryKill(process, out var terminationError);
                        processTerminationConfirmed = TryWaitForTermination(process);
                        JoinReaderThreads(outputReaderThread, errorReaderThread);
                        stopwatch.Stop();
                        var timeoutMessage = $"[DataTable] Luban build timed out after {request.TimeoutMilliseconds} ms.";
                        if (!string.IsNullOrEmpty(terminationError))
                        {
                            timeoutMessage += " Process termination also failed: " + terminationError;
                        }

                        if (!processTerminationConfirmed)
                        {
                            timeoutMessage +=
                                " Process exit could not be confirmed; the single-writer gate remains blocked. " +
                                "Confirm that all Luban processes stopped and audit the workspace lock/output before restarting the Editor.";
                        }

                        return CreateProcessResult(
                            request,
                            output,
                            error,
                            stopwatch.ElapsedMilliseconds,
                            true,
                            false,
                            -1,
                            timeoutMessage);
                    }

                    process.WaitForExit();
                    processTerminationConfirmed = true;
                    JoinReaderThreads(outputReaderThread, errorReaderThread);
                    stopwatch.Stop();

                    if (WasCancellationRequested(process))
                    {
                        return CreateProcessResult(
                            request,
                            output,
                            error,
                            stopwatch.ElapsedMilliseconds,
                            false,
                            true,
                            TryGetExitCode(process),
                            "[DataTable] Luban build was cancelled.");
                    }

                    var success = process.ExitCode == 0;
                    if (success && request.AutoRefreshAssets)
                    {
                        AssetDatabase.Refresh();
                    }

                    return CreateProcessResult(
                        request,
                        output,
                        error,
                        stopwatch.ElapsedMilliseconds,
                        false,
                        false,
                        process.ExitCode,
                        success ? null : $"[DataTable] Luban build failed with exit code {process.ExitCode}.",
                        success);
                }
                catch (Exception ex) when (IsRecoverableException(ex))
                {
                    TryKill(process, out _);
                    processTerminationConfirmed = !processStarted || TryWaitForTermination(process);
                    JoinReaderThreads(outputReaderThread, errorReaderThread);
                    stopwatch.Stop();
                    string failureMessage = $"[DataTable] Failed to run Luban build: {ex.Message}";
                    if (processStarted && !processTerminationConfirmed)
                    {
                        failureMessage +=
                            " Process exit could not be confirmed; the single-writer gate remains blocked.";
                    }

                    return CreateProcessResult(
                        request,
                        output,
                        error,
                        stopwatch.ElapsedMilliseconds,
                        false,
                        WasCancellationRequested(process),
                        TryGetExitCode(process),
                        failureMessage);
                }
                finally
                {
                    ClearActiveProcess(
                        process,
                        !processStarted || processTerminationConfirmed);
                }
            }
        }

        private static ProcessStartInfo CreateStartInfo(DataTableLubanRunRequest request)
        {
            var isWindows = Application.platform == RuntimePlatform.WindowsEditor;
            var quotedScriptPath = isWindows
                ? QuoteWindowsArgument(request.ScriptPath)
                : QuoteBashArgument(request.ScriptPath);
            var arguments = string.IsNullOrWhiteSpace(request.ScriptArguments)
                ? quotedScriptPath
                : quotedScriptPath + " " + request.ScriptArguments;

            var startInfo = new ProcessStartInfo
            {
                FileName = isWindows ? "cmd.exe" : "/bin/bash",
                Arguments = isWindows ? "/d /s /c \"" + arguments + "\"" : arguments,
                WorkingDirectory = request.WorkingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                CreateNoWindow = true
            };

            startInfo.EnvironmentVariables["CYCLONE_DATATABLE_NO_PAUSE"] = "1";
            return startInfo;
        }

        private static DataTableLubanRunResult CreateProcessResult(
            DataTableLubanRunRequest request,
            BoundedTextBuffer output,
            BoundedTextBuffer error,
            long durationMilliseconds,
            bool timedOut,
            bool cancelled,
            int exitCode,
            string errorMessage,
            bool success = false)
        {
            return new DataTableLubanRunResult(
                success,
                timedOut,
                cancelled,
                output.WasTruncated || error.WasTruncated,
                exitCode,
                request.ScriptPath,
                request.WorkingDirectory,
                output.GetText(),
                error.GetText(),
                durationMilliseconds,
                errorMessage);
        }

        private static bool TrySetActiveProcess(Process process)
        {
            lock (ActiveProcessSync)
            {
                if (_activeProcess != null)
                {
                    return false;
                }

                _cancelRequested = false;
                _activeProcess = process;
                return true;
            }
        }

        private static void ClearActiveProcess(Process process, bool terminationConfirmed)
        {
            lock (ActiveProcessSync)
            {
                if (!ReferenceEquals(_activeProcess, process))
                {
                    return;
                }

                if (!terminationConfirmed)
                {
                    _cancelRequested = true;
                    return;
                }

                _activeProcess = null;
                _cancelRequested = false;
            }
        }

        private static bool WasCancellationRequested(Process process)
        {
            lock (ActiveProcessSync)
            {
                return ReferenceEquals(_activeProcess, process) && _cancelRequested;
            }
        }

        private static int TryGetExitCode(Process process)
        {
            try
            {
                return process.HasExited ? process.ExitCode : -1;
            }
            catch (Exception exception) when (IsRecoverableException(exception))
            {
                return -1;
            }
        }

        private static bool TryWaitForTermination(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    if (!process.WaitForExit(PROCESS_TERMINATION_GRACE_MILLISECONDS))
                    {
                        return false;
                    }
                }

                if (!process.HasExited)
                {
                    return false;
                }

                process.WaitForExit();
                return true;
            }
            catch (Exception exception) when (IsRecoverableException(exception))
            {
                // The caller already reports the primary timeout/cancellation failure.
                return false;
            }
        }

        private static Thread StartReaderThread(
            StreamReader reader,
            BoundedTextBuffer destination,
            string threadName)
        {
            var thread = new Thread(() => ReadProcessStream(reader, destination))
            {
                IsBackground = true,
                Name = threadName,
            };
            thread.Start();
            return thread;
        }

        private static void ReadProcessStream(StreamReader reader, BoundedTextBuffer destination)
        {
            var buffer = new char[4096];
            try
            {
                int read;
                while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
                {
                    destination.Append(buffer, read);
                }
            }
            catch (Exception exception) when (IsRecoverableException(exception))
            {
                destination.Append("\n[DataTable] Failed to finish capturing process output: " + exception.Message + "\n");
            }
        }

        private static void JoinReaderThreads(Thread outputReaderThread, Thread errorReaderThread)
        {
            JoinReaderThread(outputReaderThread);
            JoinReaderThread(errorReaderThread);
        }

        private static void JoinReaderThread(Thread thread)
        {
            if (thread == null || !thread.IsAlive)
            {
                return;
            }

            thread.Join(PROCESS_TERMINATION_GRACE_MILLISECONDS);
        }

        private static void CancelForEditorShutdown()
        {
            // Editor shutdown and domain reload callbacks provide only a best-effort cleanup window.
            // The positive process timeout remains the deterministic upper-bound fallback.
            CancelActiveRun();
        }

        private static string ValidateScriptArguments(string arguments)
        {
            if (string.IsNullOrWhiteSpace(arguments))
            {
                return null;
            }

            for (int i = 0; i < arguments.Length; i++)
            {
                var character = arguments[i];
                if (char.IsLetterOrDigit(character) ||
                    character == ' ' ||
                    character == '\t' ||
                    character == '-' ||
                    character == '_' ||
                    character == '.' ||
                    character == '/' ||
                    character == '\\' ||
                    character == ':' ||
                    character == '=' ||
                    character == ',' ||
                    character == '@' ||
                    character == '+')
                {
                    continue;
                }

                return
                    "[DataTable] Luban script arguments contain a character that is unsafe at a shell boundary: " +
                    DescribeCharacter(character) +
                    ". Use simple option/value tokens or move complex configuration into build_config.ini.";
            }

            return null;
        }

        private static string DescribeCharacter(char character)
        {
            return char.IsControl(character)
                ? "U+" + ((int)character).ToString("X4")
                : "'" + character + "'";
        }

        private static bool IsStrictChildPath(string parentPath, string childPath)
        {
            var parent = EnsureTrailingDirectorySeparator(Path.GetFullPath(parentPath));
            var child = Path.GetFullPath(childPath);
            return child.StartsWith(parent, GetPathComparison());
        }

        private static string EnsureTrailingDirectorySeparator(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path;
            }

            var last = path[path.Length - 1];
            return last == Path.DirectorySeparatorChar || last == Path.AltDirectorySeparatorChar
                ? path
                : path + Path.DirectorySeparatorChar;
        }

        private static StringComparison GetPathComparison()
        {
            return Application.platform == RuntimePlatform.WindowsEditor
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
        }

        private static bool IsRecoverableException(Exception exception)
        {
            return !(exception is OutOfMemoryException) &&
                   !(exception is AccessViolationException) &&
                   !(exception is AppDomainUnloadedException) &&
                   !(exception is BadImageFormatException) &&
                   !(exception is CannotUnloadAppDomainException) &&
                   !(exception is StackOverflowException) &&
                   !(exception is ThreadAbortException);
        }

        private static void LogResult(DataTableLubanRunResult result, bool includeProcessOutput)
        {
            var message = BuildResultLogMessage(result, includeProcessOutput);
            var logType = ResolveResultLogType(result);
            switch (logType)
            {
                case LogType.Error:
                    Debug.LogError(message);
                    break;
                case LogType.Warning:
                    Debug.LogWarning(message);
                    break;
                default:
                    Debug.Log(message);
                    break;
            }
        }

        private static string BuildResultLogMessage(DataTableLubanRunResult result, bool includeProcessOutput)
        {
            var builder = new StringBuilder(4096);
            if (result.Success)
            {
                builder.AppendLine($"[DataTable] Luban build completed in {result.DurationMilliseconds} ms.");
            }
            else if (!string.IsNullOrEmpty(result.ErrorMessage))
            {
                builder.AppendLine(result.ErrorMessage);
            }
            else
            {
                builder.AppendLine("[DataTable] Luban build failed.");
            }

            builder.AppendLine($"  Exit code  : {result.ExitCode}");
            builder.AppendLine($"  Timed out  : {result.TimedOut}");
            builder.AppendLine($"  Cancelled  : {result.Cancelled}");
            builder.AppendLine($"  Truncated  : {result.OutputTruncated}");
            builder.AppendLine($"  Script     : {result.ScriptPath}");
            builder.AppendLine($"  Working dir: {result.WorkingDirectory}");

            AppendDiagnosticsSection(builder, result);

            if (includeProcessOutput)
            {
                AppendOutputSection(builder, "Output", result.StandardOutput);
                AppendOutputSection(builder, "Error", result.StandardError);
            }

            return TrimUnityLogMessage(builder.ToString().TrimEnd());
        }

        private static void AppendDiagnosticsSection(StringBuilder builder, DataTableLubanRunResult result)
        {
            var diagnostics = BuildDiagnosticsMessage(result);
            if (string.IsNullOrWhiteSpace(diagnostics))
            {
                return;
            }

            builder.AppendLine();
            builder.AppendLine("---- Diagnostics ----");
            builder.Append(diagnostics.TrimEnd());
            builder.AppendLine();
        }

        private static string BuildDiagnosticsMessage(DataTableLubanRunResult result)
        {
            var processText = CombineProcessText(result);
            if (string.IsNullOrWhiteSpace(processText))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(768);
            if (ContainsNamespaceCaseConflict(processText))
            {
                AppendNamespaceCaseConflictDiagnostic(builder, processText);
            }

            if (LooksLikeMojibake(processText))
            {
                AppendEncodingDiagnostic(builder);
            }

            return builder.ToString();
        }

        private static string CombineProcessText(DataTableLubanRunResult result)
        {
            var builder = new StringBuilder();
            AppendIfNotEmpty(builder, result.ErrorMessage);
            AppendIfNotEmpty(builder, result.StandardOutput);
            AppendIfNotEmpty(builder, result.StandardError);
            return builder.ToString();
        }

        private static void AppendIfNotEmpty(StringBuilder builder, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(value);
        }

        private static bool ContainsNamespaceCaseConflict(string processText)
        {
            return processText.IndexOf(NamespaceCaseConflictMarker, StringComparison.Ordinal) >= 0 ||
                   processText.IndexOf(NamespaceCaseConflictMojibakeMarker, StringComparison.Ordinal) >= 0;
        }

        private static bool LooksLikeMojibake(string processText)
        {
            return processText.IndexOf(NamespaceCaseConflictMojibakeMarker, StringComparison.Ordinal) >= 0 ||
                   processText.IndexOf(WindowsPlatformMojibakeMarker, StringComparison.Ordinal) >= 0;
        }

        private static void AppendNamespaceCaseConflictDiagnostic(StringBuilder builder, string processText)
        {
            builder.AppendLine("[DataTable] Luban namespace case conflict detected.");
            builder.AppendLine("Reason: Luban maps type, table, bean, and enum namespaces to generated code folders. Windows file systems are case-insensitive, so `Test` and `test` resolve to the same folder and generated code can overwrite itself. Luban stops the build to avoid that.");

            var typeNames = ExtractLubanTypeNames(processText);
            if (typeNames.Count > 0)
            {
                builder.AppendLine("Conflicting types:");
                for (int i = 0; i < typeNames.Count; i++)
                {
                    builder.AppendLine("  - " + typeNames[i]);
                }
            }

            builder.AppendLine("Suggested fixes:");
            builder.AppendLine("  1. Check `DataTable/Datas/__tables__.xlsx`, `__beans__.xlsx`, `__enums__.xlsx`, and business table field types.");
            builder.AppendLine("  2. Keep namespace casing consistent; this project usually uses lower-case module names, for example `test.TbTestData` and `test.ETestQuality`.");
            builder.AppendLine("  3. Delete the old generated code folder before running Luban again, so stale differently-cased `.cs` files do not keep breaking compilation.");
        }

        private static List<string> ExtractLubanTypeNames(string processText)
        {
            var matches = LubanTypeRegex.Matches(processText);
            var typeNames = new List<string>(matches.Count);
            var seenTypeNames = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < matches.Count; i++)
            {
                var typeName = matches[i].Groups[1].Value;
                if (string.IsNullOrEmpty(typeName) || !seenTypeNames.Add(typeName))
                {
                    continue;
                }

                typeNames.Add(typeName);
            }

            return typeNames;
        }

        private static void AppendEncodingDiagnostic(StringBuilder builder)
        {
            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.AppendLine("[DataTable] Process output looks like an encoding mismatch.");
            builder.AppendLine("The runner now decodes Luban stdout/stderr as UTF-8. If text is still garbled, make sure the `.bat` file does not force a non-UTF-8 code page, or add `chcp 65001 > nul` near the top of the script.");
        }

        private static void AppendOutputSection(StringBuilder builder, string title, string content)
        {
            if (string.IsNullOrWhiteSpace(content))
            {
                return;
            }

            builder.AppendLine();
            builder.AppendLine("---- " + title + " ----");
            builder.Append(content.TrimEnd());
            builder.AppendLine();
        }

        private static LogType ResolveResultLogType(DataTableLubanRunResult result)
        {
            if (!result.Success)
            {
                return LogType.Error;
            }

            if (!string.IsNullOrWhiteSpace(result.StandardError) ||
                ContainsWarning(result.StandardOutput) ||
                ContainsWarning(result.StandardError))
            {
                return LogType.Warning;
            }

            return LogType.Log;
        }

        private static bool ContainsWarning(string value)
        {
            return !string.IsNullOrEmpty(value) &&
                   (value.StartsWith("[WARN]", StringComparison.Ordinal) ||
                    value.IndexOf("|WARN|", StringComparison.Ordinal) >= 0 ||
                    value.IndexOf("warning", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static string TrimUnityLogMessage(string message)
        {
            if (string.IsNullOrEmpty(message) || message.Length <= MAX_UNITY_LOG_CHARACTERS)
            {
                return message;
            }

            const string marker = "\n\n---- Unity log truncated; full process output is available from DataTableLubanRunResult. ----\n\n";
            var headLength = MAX_UNITY_LOG_CHARACTERS / 2;
            var tailLength = MAX_UNITY_LOG_CHARACTERS - headLength - marker.Length;
            if (tailLength <= 0)
            {
                return message.Substring(0, MAX_UNITY_LOG_CHARACTERS);
            }

            return message.Substring(0, headLength) + marker + message.Substring(message.Length - tailLength);
        }

        private static bool TryKill(Process process, out string errorMessage)
        {
            errorMessage = null;
            try
            {
                if (process.HasExited)
                {
                    return true;
                }

                var killTreeMethod = typeof(Process).GetMethod(
                    "Kill",
                    BindingFlags.Instance | BindingFlags.Public,
                    null,
                    new[] { typeof(bool) },
                    null);
                if (killTreeMethod != null)
                {
                    try
                    {
                        killTreeMethod.Invoke(process, new object[] { true });
                        return true;
                    }
                    catch (Exception exception) when (IsRecoverableException(exception.GetBaseException()))
                    {
                        // Older runtimes can expose the overload without supporting tree termination.
                        // Fall back to terminating the directly owned shell process.
                    }
                }

                process.Kill();
                return true;
            }
            catch (Exception ex) when (IsRecoverableException(ex))
            {
                errorMessage = ex.GetBaseException().Message;
                return false;
            }
        }

        private sealed class BoundedTextBuffer
        {
            private const string TruncationMarker = "\n[DataTable] Process output truncated by the configured capture budget.\n";

            private readonly object _sync = new object();
            private readonly StringBuilder _builder;
            private readonly int _maximumCharacters;
            private bool _wasTruncated;

            public BoundedTextBuffer(int maximumCharacters)
            {
                _maximumCharacters = Math.Max(1, maximumCharacters);
                _builder = new StringBuilder(Math.Min(_maximumCharacters, 4096));
            }

            public bool WasTruncated
            {
                get
                {
                    lock (_sync)
                    {
                        return _wasTruncated;
                    }
                }
            }

            public void Append(string value)
            {
                if (string.IsNullOrEmpty(value))
                {
                    return;
                }

                char[] characters = value.ToCharArray();
                Append(characters, characters.Length);
            }

            public void Append(char[] characters, int count)
            {
                if (characters == null || count <= 0)
                {
                    return;
                }

                lock (_sync)
                {
                    if (_builder.Length >= _maximumCharacters)
                    {
                        _wasTruncated = true;
                        return;
                    }

                    var remaining = _maximumCharacters - _builder.Length;
                    var length = Math.Min(count, remaining);
                    _builder.Append(characters, 0, length);

                    if (length != count)
                    {
                        _wasTruncated = true;
                    }
                }
            }

            public string GetText()
            {
                lock (_sync)
                {
                    return _wasTruncated
                        ? _builder + TruncationMarker
                        : _builder.ToString();
                }
            }
        }

        private static string QuoteWindowsArgument(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static string QuoteBashArgument(string value)
        {
            return "'" + value.Replace("'", "'\\''") + "'";
        }
    }
}
