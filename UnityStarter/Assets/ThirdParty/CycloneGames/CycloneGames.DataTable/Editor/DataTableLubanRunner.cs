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
        public DataTableSettings Settings { get; set; }
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

        /// <summary>
        /// Override for the Luban project directory. Kept for CI compatibility with older callers.
        /// </summary>
        public static string ProjectDirOverride { get; set; }

        /// <summary>
        /// Override for the Luban script name. Kept for CI compatibility with older callers.
        /// </summary>
        public static string ScriptNameOverride { get; set; }

        public static string EffectiveProjectDir =>
            ProjectDirOverride ?? DataTableSettings.GetOrCreate().GetDataTableProjectDir();

        public static string EffectiveScriptName =>
            ScriptNameOverride ?? DataTableSettings.GetOrCreate().GetScriptName();

        [MenuItem(MenuPath)]
        public static void Run()
        {
            var result = RunWithResult();
            LogResult(result);
        }

        /// <summary>
        /// Run the default DataTableSettings and return a structured result.
        /// </summary>
        public static DataTableLubanRunResult RunWithResult()
        {
            return RunWithResult(DataTableSettings.GetOrCreate());
        }

        /// <summary>
        /// Run a derived settings type without requiring package modification.
        /// </summary>
        public static DataTableLubanRunResult RunWithResult<TSettings>()
            where TSettings : DataTableSettings
        {
            return RunWithResult(DataTableSettings.GetOrCreate<TSettings>());
        }

        /// <summary>
        /// Run a settings instance. Derived settings can override CreateLubanRunRequest.
        /// </summary>
        public static DataTableLubanRunResult RunWithResult(DataTableSettings settings)
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
                return new DataTableLubanRunResult(
                    false,
                    false,
                    -1,
                    request.ScriptPath,
                    request.WorkingDirectory,
                    string.Empty,
                    string.Empty,
                    0,
                    validationError);
            }

            return RunProcess(request);
        }

        /// <summary>
        /// Build a request from settings. Custom projects can call this and then modify selected fields.
        /// </summary>
        public static DataTableLubanRunRequest CreateRequest(DataTableSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            var projectRoot = settings.GetProjectRoot();
            var projectDir = ProjectDirOverride ?? settings.GetDataTableProjectDir();
            var scriptName = ScriptNameOverride ?? settings.GetScriptName();
            var scriptExtension = settings.GetScriptExtension();
            var workingDirectory = Path.GetFullPath(Path.Combine(projectRoot, projectDir));

            return new DataTableLubanRunRequest
            {
                Settings = settings,
                ProjectRoot = projectRoot,
                ProjectDirectory = projectDir,
                ScriptName = scriptName,
                ScriptExtension = scriptExtension,
                ScriptArguments = settings.GetScriptArguments(),
                WorkingDirectory = workingDirectory,
                ScriptPath = Path.Combine(workingDirectory, scriptName + scriptExtension),
                ConfigAssetPath = AssetDatabase.GetAssetPath(settings),
                TimeoutMilliseconds = settings.GetTimeoutMilliseconds(),
                AutoRefreshAssets = settings.ShouldRefreshAssets()
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

            return new ProcessStartInfo
            {
                FileName = isWindows ? "cmd.exe" : "/bin/bash",
                Arguments = isWindows ? "/c " + arguments : arguments,
                WorkingDirectory = request.WorkingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
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
            if (!request.LogOutputToUnity)
            {
                return;
            }

            if (isError)
            {
                Debug.LogError("[Luban] " + line);
            }
            else
            {
                Debug.Log("[Luban] " + line);
            }
        }

        private static void LogResult(DataTableLubanRunResult result)
        {
            if (result.Success)
            {
                Debug.Log(
                    $"[DataTable] Luban build completed in {result.DurationMilliseconds} ms.\n" +
                    $"  Script: {result.ScriptPath}\n" +
                    $"  Working dir: {result.WorkingDirectory}");
                return;
            }

            Debug.LogError(result.ErrorMessage);
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
