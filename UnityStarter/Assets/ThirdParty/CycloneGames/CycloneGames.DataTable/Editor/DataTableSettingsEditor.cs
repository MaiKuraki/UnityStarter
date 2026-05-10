using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.DataTable.Unity.Editor
{
    [CustomEditor(typeof(DataTableSettings), true)]
    public class DataTableSettingsEditor : UnityEditor.Editor
    {
        private SerializedProperty _dataTableProjectDir;
        private SerializedProperty _scriptName;
        private SerializedProperty _autoRefreshAssets;

        private static readonly GUIContent RefreshLabel = new GUIContent("Auto Refresh Assets");
        private static readonly GUIContent ScriptNameLabel = new GUIContent("Script Name");

        private static GUIStyle _richMiniLabel;
        private static GUIStyle _richWrapMiniLabel;
        private static GUIStyle RichMiniLabel => _richMiniLabel ?? (_richMiniLabel = new GUIStyle(EditorStyles.miniLabel) { richText = true });
        private static GUIStyle RichWrapMiniLabel => _richWrapMiniLabel ?? (_richWrapMiniLabel = new GUIStyle(EditorStyles.miniLabel) { richText = true, wordWrap = true });

        private void OnEnable()
        {
            _dataTableProjectDir = serializedObject.FindProperty("DataTableProjectDir");
            _scriptName = serializedObject.FindProperty("ScriptName");
            _autoRefreshAssets = serializedObject.FindProperty("AutoRefreshAssets");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawSettingsHeader();
            EditorGUILayout.Space(5);

            DrawProjectDirField();
            EditorGUILayout.Space(5);

            DrawScriptNameField();
            EditorGUILayout.Space(5);

            DrawAutoRefreshField();
            EditorGUILayout.Space(10);

            DrawPathPreview();
            EditorGUILayout.Space(10);

            DrawValidation();
            EditorGUILayout.Space(10);

            DrawActions();

            serializedObject.ApplyModifiedProperties();
        }

        private static void DrawSettingsHeader()
        {
            EditorGUILayout.LabelField("DataTable Settings", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Module-level settings for the DataTable pipeline. Currently configures the " +
                "Luban code-generation step. You can place this asset anywhere under Assets/ — " +
                "the system will auto-discover it. Only one instance should exist in the project.",
                MessageType.Info);
        }

        private void DrawProjectDirField()
        {
            EditorGUILayout.PropertyField(_dataTableProjectDir);
            var settings = (DataTableSettings)target;
            var projectRoot = GetProjectRoot();
            var resolved = Path.GetFullPath(Path.Combine(projectRoot, settings.DataTableProjectDir));
            var dirExists = Directory.Exists(resolved);

            EditorGUI.indentLevel++;
            if (dirExists)
            {
                EditorGUILayout.LabelField(
                    new GUIContent($"<color=#5a5>✓ Dir exists: {resolved}</color>"),
                    RichWrapMiniLabel);
            }
            else
            {
                EditorGUILayout.LabelField(
                    new GUIContent($"<color=#c55>✗ Dir not found: {resolved}</color>"),
                    RichWrapMiniLabel);
                EditorGUILayout.HelpBox(
                    "The directory above does not exist on disk. The build will fail unless you " +
                    "create it with a valid Luban project (including Excel/ and build scripts).",
                    MessageType.Warning);
            }

            EditorGUILayout.LabelField(
                new GUIContent($"<color=#888>Project root: {projectRoot}</color>"),
                RichWrapMiniLabel);
            EditorGUI.indentLevel--;
        }

        private void DrawScriptNameField()
        {
            EditorGUILayout.PropertyField(_scriptName, ScriptNameLabel);
            var settings = (DataTableSettings)target;
            var projectRoot = GetProjectRoot();
            var resolvedDir = Path.GetFullPath(Path.Combine(projectRoot, settings.DataTableProjectDir));
            var ext = Application.platform == RuntimePlatform.WindowsEditor ? ".bat" : ".sh";
            var fullScriptPath = Path.Combine(resolvedDir, settings.ScriptName + ext);

            EditorGUI.indentLevel++;

            var scriptExists = File.Exists(fullScriptPath);
            if (Directory.Exists(resolvedDir))
            {
                if (scriptExists)
                {
                    EditorGUILayout.LabelField(
                        new GUIContent($"<color=#5a5>✓ Script found: {fullScriptPath}</color>"),
                        RichMiniLabel);
                }
                else
                {
                    EditorGUILayout.LabelField(
                        new GUIContent($"<color=#c55>✗ Script not found: {fullScriptPath}</color>"),
                        RichMiniLabel);

                    var dirInfo = new DirectoryInfo(resolvedDir);
                    if (dirInfo.Exists)
                    {
                        var scripts = dirInfo.GetFiles("*.bat")
                            .Select(f => f.Name)
                            .Union(dirInfo.GetFiles("*.sh").Select(f => f.Name))
                            .ToArray();

                        foreach (var script in scripts)
                        {
                            EditorGUILayout.LabelField(
                                new GUIContent($"   └ <color=#888>{script}</color> (found in directory)"),
                                RichMiniLabel);
                        }

                        if (scripts.Length > 0)
                        {
                            EditorGUILayout.HelpBox(
                                "The directory contains build scripts but none match the configured " +
                                "name. Did you mean one of the scripts listed above?",
                                MessageType.Warning);
                        }
                        else
                        {
                            EditorGUILayout.HelpBox(
                                "No .bat or .sh scripts found in the directory. Make sure your Luban " +
                                "project is set up correctly with build scripts.",
                                MessageType.Error);
                        }
                    }
                }
            }
            EditorGUI.indentLevel--;
        }

        private void DrawAutoRefreshField()
        {
            EditorGUILayout.PropertyField(_autoRefreshAssets, RefreshLabel);

            EditorGUI.indentLevel++;
            EditorGUILayout.HelpBox(
                "When enabled, AssetDatabase.Refresh() is called automatically after a successful " +
                "Luban build to import newly generated .bytes files and C# scripts.\n\n" +
                "This does NOT trigger the Luban build itself — you must still run " +
                "Tools > CycloneGames > DataTable > Run Luban Build manually (or invoke " +
                "DataTableLubanRunner.Run() from a CI script).\n\n" +
                "Disable this if you prefer to refresh assets manually, or if your build " +
                "pipeline handles asset refresh separately.",
                MessageType.Info);
            EditorGUI.indentLevel--;
        }

        private void DrawPathPreview()
        {
            EditorGUILayout.LabelField("Resolved Build Script", EditorStyles.boldLabel);

            var settings = (DataTableSettings)target;
            var projectRoot = GetProjectRoot();
            var resolvedDir = Path.GetFullPath(Path.Combine(projectRoot, settings.DataTableProjectDir));
            var ext = Application.platform == RuntimePlatform.WindowsEditor ? ".bat" : ".sh";
            var scriptPath = Path.Combine(resolvedDir, settings.ScriptName + ext);

            EditorGUILayout.HelpBox(
                "These are the resolved paths based on your configuration above. " +
                "They show exactly where the build step will look for scripts on this machine.",
                MessageType.None);

            var savedWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 105f;

            EditorGUILayout.LabelField("Project Root", projectRoot);
            EditorGUILayout.LabelField("Project Dir", settings.DataTableProjectDir);
            EditorGUILayout.LabelField("Resolved Dir", resolvedDir);
            EditorGUILayout.LabelField("Script Path", scriptPath);

            EditorGUIUtility.labelWidth = savedWidth;

            EditorGUILayout.Space(3);

            var platform = Application.platform == RuntimePlatform.WindowsEditor
                ? "Windows (cmd.exe)"
                : "macOS/Linux (/bin/bash)";
            EditorGUILayout.HelpBox(
                $"Platform  : {platform}\n" +
                $"Work Dir  : {resolvedDir}\n" +
                $"Execute   : {new FileInfo(scriptPath).Name}",
                MessageType.Info);
        }

        private void DrawValidation()
        {
            EditorGUILayout.LabelField("Validation", EditorStyles.boldLabel);

            var settings = (DataTableSettings)target;
            var projectRoot = GetProjectRoot();
            var resolvedDir = Path.GetFullPath(Path.Combine(projectRoot, settings.DataTableProjectDir));
            var ext = Application.platform == RuntimePlatform.WindowsEditor ? ".bat" : ".sh";
            var scriptPath = Path.Combine(resolvedDir, settings.ScriptName + ext);

            var dirExists = Directory.Exists(resolvedDir);
            var scriptExists = File.Exists(scriptPath);

            if (dirExists && scriptExists)
            {
                EditorGUILayout.HelpBox(
                    "Ready. The configured Luban project directory and build script both exist.",
                    MessageType.Info);
            }
            else if (dirExists && !scriptExists)
            {
                EditorGUILayout.HelpBox(
                    "The Luban project directory exists but the build script was not found. " +
                    "Check the Script Name field — does it match your .bat/.sh file?",
                    MessageType.Warning);
            }
            else if (!dirExists)
            {
                EditorGUILayout.HelpBox(
                    "The Luban project directory does not exist. Create it and set up a Luban " +
                    "project with Excel files and build scripts. See the module README for setup instructions.",
                    MessageType.Warning);
            }

            if (settings.DataTableProjectDir.Contains(".."))
            {
                if (!resolvedDir.StartsWith(projectRoot) && !Directory.Exists(resolvedDir))
                {
                    EditorGUILayout.HelpBox(
                        "The configured path uses '..' to point outside the project. This is normal " +
                        "for multi-repo setups but ensure the directory actually exists.",
                        MessageType.Warning);
                }
            }
        }

        private void DrawActions()
        {
            EditorGUILayout.LabelField("Quick Actions", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Reveal Settings Asset"))
            {
                var path = AssetDatabase.GetAssetPath(target);
                EditorUtility.RevealInFinder(path);
            }

            if (GUILayout.Button("Run Luban Build"))
            {
                DataTableLubanRunner.Run();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Open Project Directory"))
            {
                var settings = (DataTableSettings)target;
                var projectRoot = GetProjectRoot();
                var resolvedDir = Path.GetFullPath(Path.Combine(projectRoot, settings.DataTableProjectDir));
                if (Directory.Exists(resolvedDir))
                    EditorUtility.RevealInFinder(resolvedDir);
                else
                    EditorUtility.DisplayDialog(
                        "Directory Not Found",
                        $"The directory does not exist:\n{resolvedDir}\n\nCreate it first.",
                        "OK");
            }

            EditorGUILayout.EndHorizontal();
        }

        private static string GetProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }
    }
}
