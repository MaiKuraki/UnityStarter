using System.IO;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.DataTable.Unity.Editor
{
    /// <summary>
    /// Menu item and programmatic entry point for running the Luban code-generation script.
    /// <para>
    /// Configuration is read from <see cref="DataTableSettings"/> (ScriptableObject).
    /// </para>
    /// <para>
    /// This runner can be invoked from:
    /// <list type="bullet">
    ///   <item>The Unity Editor menu: Tools → CycloneGames → DataTable → Run Luban Build</item>
    ///   <item>A CI / build script: <c>DataTableLubanRunner.Run()</c></item>
    ///   <item>A pre-build callback: implement <c>IPreprocessBuildWithReport</c> and call <c>Run()</c></item>
    /// </list>
    /// </para>
    /// <para>
    /// For programmatic overrides (CI pipelines), set <see cref="ProjectDirOverride"/> or
    /// <see cref="ScriptNameOverride"/>. These take precedence over the SO config.
    /// Set to null to restore SO-driven behavior.
    /// </para>
    /// </summary>
    public static class DataTableLubanRunner
    {
        private const string MenuPath = "Tools/CycloneGames/DataTable/Run Luban Build";

        /// <summary>
        /// Override for the Luban project directory. If non-null, takes precedence
        /// over <see cref="DataTableSettings.DataTableProjectDir"/>.
        /// </summary>
        public static string ProjectDirOverride { get; set; }

        /// <summary>
        /// Override for the Luban script name. If non-null, takes precedence
        /// over <see cref="DataTableSettings.ScriptName"/>.
        /// </summary>
        public static string ScriptNameOverride { get; set; }

        /// <summary>Resolved effective project directory (override or SO config).</summary>
        public static string EffectiveProjectDir =>
            ProjectDirOverride ?? DataTableSettings.GetOrCreate().DataTableProjectDir;

        /// <summary>Resolved effective script name (override or SO config).</summary>
        public static string EffectiveScriptName =>
            ScriptNameOverride ?? DataTableSettings.GetOrCreate().ScriptName;

        [MenuItem(MenuPath)]
        public static void Run()
        {
            var config = DataTableSettings.GetOrCreate();

            var projectDir = ProjectDirOverride ?? config.DataTableProjectDir;
            var scriptName = ScriptNameOverride ?? config.ScriptName;

            var projectRoot = Path.GetDirectoryName(Application.dataPath) ?? ".";
            var configDir = Path.GetFullPath(Path.Combine(projectRoot, projectDir));
            var scriptExtension = Application.platform == RuntimePlatform.WindowsEditor ? ".bat" : ".sh";
            var scriptPath = Path.Combine(configDir, scriptName + scriptExtension);

            if (!File.Exists(scriptPath))
            {
                var configPath = DataTableSettings.AssetPath;
                Debug.LogError(
                    $"[DataTable] Luban build script not found:\n" +
                    $"  Script path : {scriptPath}\n" +
                    $"  Config asset: {(string.IsNullOrEmpty(configPath) ? "(not found)" : configPath)}\n\n" +
                    "To fix this:\n" +
                    "  1. Install Luban (https://github.com/focus-creative-games/luban)\n" +
                    "  2. Set up a Luban project in the directory configured above\n" +
                    "  3. Edit the build config asset to match your project layout\n" +
                    "     (menu: Assets > Create > CycloneGames > DataTable > Build Config)\n" +
                    "  4. Ensure the build script (.bat/.sh) exists in that directory\n\n" +
                    "If you use a different config pipeline, ignore this menu item.");
                return;
            }

            Debug.Log($"[DataTable] Running Luban build:\n  Script: {scriptPath}\n  Working dir: {configDir}");

            var process = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = Application.platform == RuntimePlatform.WindowsEditor
                        ? "cmd.exe"
                        : "/bin/bash",
                    Arguments = Application.platform == RuntimePlatform.WindowsEditor
                        ? $"/c \"{scriptPath}\""
                        : $"\"{scriptPath}\"",
                    WorkingDirectory = configDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.OutputDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    Debug.Log($"[Luban] {e.Data}");
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    Debug.LogError($"[Luban] {e.Data}");
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();

            if (process.ExitCode == 0)
            {
                if (config.AutoRefreshAssets)
                {
                    AssetDatabase.Refresh();
                    Debug.Log("[DataTable] Luban build completed. Assets refreshed.");
                }
                else
                {
                    Debug.Log(
                        "[DataTable] Luban build completed. Asset refresh skipped (config.AutoRefreshAssets = false). " +
                        "Refresh manually if needed.");
                }
            }
            else
            {
                Debug.LogError(
                    $"[DataTable] Luban build failed with exit code {process.ExitCode}. " +
                    "Check the error output above for details.");
            }
        }
    }
}
