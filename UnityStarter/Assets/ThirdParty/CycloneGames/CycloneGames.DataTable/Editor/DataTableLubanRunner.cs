using System;
using System.Diagnostics;
using System.IO;
using System.Text;
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
        public int TimeoutMilliseconds { get; set; }
        public bool AutoRefreshAssets { get; set; }
        public bool LogOutputToUnity { get; set; } = true;

        /// <summary>
        /// Whether to stream Luban output into the Unity Console line by line.
        /// Disabled by default to avoid noisy generation logs.
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
        {
            Success = success;
            TimedOut = timedOut;
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
    public static class DataTableLubanRunner
    {
        private const string MenuPath = "Tools/CycloneGames/DataTable/Run Luban Build";
        private const string LubanLogPrefix = "[Luban]";
        private const string DataTableLogPrefix = "[DataTable]";
        private const int MAX_UNITY_LOG_CHARACTERS = 60000;

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
                LogResult(result, !request.StreamOutputToUnity);
            }

            return result;
        }

        /// <summary>
        /// Build a request from settings. Custom projects can call this and then modify selected fields.
        /// </summary>
        public static DataTableLubanRunRequest CreateRequest(DataTableLubanSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            var projectRoot = settings.GetProjectRoot();
            var projectDir = LubanProjectDirOverride ?? settings.GetLubanProjectDir() ?? string.Empty;
            var scriptName = LubanScriptNameOverride ?? settings.GetLubanScriptName() ?? string.Empty;
            var scriptExtension = settings.GetLubanScriptExtension() ?? string.Empty;
            var workingDirectory = string.IsNullOrWhiteSpace(projectRoot)
                ? string.Empty
                : Path.GetFullPath(string.IsNullOrWhiteSpace(projectDir)
                    ? projectRoot
                    : Path.Combine(projectRoot, projectDir));
            var scriptPath = string.IsNullOrWhiteSpace(workingDirectory) || string.IsNullOrWhiteSpace(scriptName)
                ? string.Empty
                : Path.Combine(workingDirectory, scriptName + scriptExtension);

            return new DataTableLubanRunRequest
            {
                Settings = settings,
                ProjectRoot = projectRoot,
                ProjectDirectory = projectDir,
                ScriptName = scriptName,
                ScriptExtension = scriptExtension,
                ScriptArguments = settings.GetLubanScriptArguments(),
                WorkingDirectory = workingDirectory,
                ScriptPath = scriptPath,
                ConfigAssetPath = AssetDatabase.GetAssetPath(settings),
                TimeoutMilliseconds = settings.GetLubanTimeoutMilliseconds(),
                AutoRefreshAssets = settings.ShouldRefreshAssetsAfterLubanBuild()
            };
        }

        public static string ValidateRequest(DataTableLubanRunRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));

            if (string.IsNullOrWhiteSpace(request.WorkingDirectory))
            {
                return "[DataTable] Luban working directory is empty.";
            }

            if (string.IsNullOrWhiteSpace(request.ScriptName))
            {
                return "[DataTable] Luban script name is empty.";
            }

            if (request.ScriptName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return $"[DataTable] Luban script name contains invalid file name characters: {request.ScriptName}";
            }

            if (!Directory.Exists(request.WorkingDirectory))
            {
                return $"[DataTable] Luban working directory not found: {request.WorkingDirectory}";
            }

            if (string.IsNullOrWhiteSpace(request.ScriptPath) || !File.Exists(request.ScriptPath))
            {
                return
                    "[DataTable] Luban build script not found:\n" +
                    $"  Script path : {request.ScriptPath}\n" +
                    $"  Config asset: {(string.IsNullOrEmpty(request.ConfigAssetPath) ? "(not found)" : request.ConfigAssetPath)}";
            }

            return null;
        }

        private static DataTableLubanRunResult RunProcess(DataTableLubanRunRequest request)
        {
            var output = new StringBuilder(4096);
            var error = new StringBuilder(1024);
            var stopwatch = Stopwatch.StartNew();

            using (var process = new Process())
            {
                process.StartInfo = CreateStartInfo(request);
                process.OutputDataReceived += (_, e) => AppendProcessLine(output, request, e.Data, false);
                process.ErrorDataReceived += (_, e) => AppendProcessLine(error, request, e.Data, true);

                try
                {
                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    var completed = request.TimeoutMilliseconds <= 0
                        ? WaitForExit(process)
                        : process.WaitForExit(request.TimeoutMilliseconds);

                    if (!completed)
                    {
                        TryKill(process);
                        stopwatch.Stop();
                        return new DataTableLubanRunResult(
                            false,
                            true,
                            -1,
                            request.ScriptPath,
                            request.WorkingDirectory,
                            output.ToString(),
                            error.ToString(),
                            stopwatch.ElapsedMilliseconds,
                            $"[DataTable] Luban build timed out after {request.TimeoutMilliseconds} ms.");
                    }

                    process.WaitForExit();
                    stopwatch.Stop();

                    var success = process.ExitCode == 0;
                    if (success && request.AutoRefreshAssets)
                    {
                        AssetDatabase.Refresh();
                    }

                    return new DataTableLubanRunResult(
                        success,
                        false,
                        process.ExitCode,
                        request.ScriptPath,
                        request.WorkingDirectory,
                        output.ToString(),
                        error.ToString(),
                        stopwatch.ElapsedMilliseconds,
                        success ? null : $"[DataTable] Luban build failed with exit code {process.ExitCode}.");
                }
                catch (Exception ex)
                {
                    stopwatch.Stop();
                    return new DataTableLubanRunResult(
                        false,
                        false,
                        -1,
                        request.ScriptPath,
                        request.WorkingDirectory,
                        output.ToString(),
                        error.ToString(),
                        stopwatch.ElapsedMilliseconds,
                        $"[DataTable] Failed to run Luban build: {ex.Message}");
                }
            }
        }

        private static ProcessStartInfo CreateStartInfo(DataTableLubanRunRequest request)
        {
            var isWindows = Application.platform == RuntimePlatform.WindowsEditor;
            var quotedScriptPath = QuoteArgument(request.ScriptPath);
            var arguments = string.IsNullOrWhiteSpace(request.ScriptArguments)
                ? quotedScriptPath
                : quotedScriptPath + " " + request.ScriptArguments;

            var startInfo = new ProcessStartInfo
            {
                FileName = isWindows ? "cmd.exe" : "/bin/bash",
                Arguments = isWindows ? "/c " + arguments : arguments,
                WorkingDirectory = request.WorkingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            startInfo.EnvironmentVariables["CYCLONE_DATATABLE_NO_PAUSE"] = "1";
            return startInfo;
        }

        private static bool WaitForExit(Process process)
        {
            process.WaitForExit();
            return true;
        }

        private static void AppendProcessLine(
            StringBuilder buffer,
            DataTableLubanRunRequest request,
            string line,
            bool isError)
        {
            if (string.IsNullOrEmpty(line))
            {
                return;
            }

            buffer.AppendLine(line);
            if (!request.LogOutputToUnity || !request.StreamOutputToUnity)
            {
                return;
            }

            var message = FormatProcessLogLine(line);
            var logType = ResolveProcessLogType(line, isError);
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

        private static string FormatProcessLogLine(string line)
        {
            if (line.StartsWith(LubanLogPrefix, StringComparison.Ordinal) ||
                line.StartsWith(DataTableLogPrefix, StringComparison.Ordinal))
            {
                return line;
            }

            return LubanLogPrefix + " " + line;
        }

        private static LogType ResolveProcessLogType(string line, bool isError)
        {
            if (isError ||
                line.StartsWith("[ERROR]", StringComparison.Ordinal) ||
                line.IndexOf("|ERROR|", StringComparison.Ordinal) >= 0 ||
                line.IndexOf("Build FAILED", StringComparison.Ordinal) >= 0)
            {
                return LogType.Error;
            }

            if (line.StartsWith("[WARN]", StringComparison.Ordinal) ||
                line.IndexOf("|WARN|", StringComparison.Ordinal) >= 0)
            {
                return LogType.Warning;
            }

            return LogType.Log;
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
            builder.AppendLine($"  Script     : {result.ScriptPath}");
            builder.AppendLine($"  Working dir: {result.WorkingDirectory}");

            if (includeProcessOutput)
            {
                AppendOutputSection(builder, "Output", result.StandardOutput);
                AppendOutputSection(builder, "Error", result.StandardError);
            }

            return TrimUnityLogMessage(builder.ToString().TrimEnd());
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

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[DataTable] Failed to kill timed-out Luban process: " + ex.Message);
            }
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
