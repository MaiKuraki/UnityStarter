using System.IO;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.DataTable.Unity.Editor
{
    [CustomEditor(typeof(DataTableSettings), true)]
    public class DataTableSettingsEditor : UnityEditor.Editor
    {
        private SerializedProperty _dataTableProjectDir;
        private SerializedProperty _scriptName;
        private SerializedProperty _scriptArguments;
        private SerializedProperty _timeoutSeconds;
        private SerializedProperty _autoRefreshAssets;

        private DataTableLubanRunRequest _cachedRequest;
        private string _cachedValidationError;
        private string[] _cachedScripts = System.Array.Empty<string>();
        private int _lastTargetHash;
        private bool _showResolvedBuildScript;

        private static readonly GUIContent ProjectDirLabel = new GUIContent("Project Dir");
        private static readonly GUIContent ScriptNameLabel = new GUIContent("Script Name");
        private static readonly GUIContent ScriptArgumentsLabel = new GUIContent("Script Arguments");
        private static readonly GUIContent TimeoutLabel = new GUIContent("Timeout Seconds");
        private static readonly GUIContent RefreshLabel = new GUIContent("Auto Refresh Assets");

        private static GUIStyle _richMiniLabel;
        private static GUIStyle _richWrapMiniLabel;

        protected static GUIStyle RichMiniLabel =>
            _richMiniLabel ?? (_richMiniLabel = new GUIStyle(EditorStyles.miniLabel) { richText = true });

        protected static GUIStyle RichWrapMiniLabel =>
            _richWrapMiniLabel ?? (_richWrapMiniLabel = new GUIStyle(EditorStyles.miniLabel) { richText = true, wordWrap = true });

        protected virtual void OnEnable()
        {
            _dataTableProjectDir = serializedObject.FindProperty("DataTableProjectDir");
            _scriptName = serializedObject.FindProperty("ScriptName");
            _scriptArguments = serializedObject.FindProperty("ScriptArguments");
            _timeoutSeconds = serializedObject.FindProperty("TimeoutSeconds");
            _autoRefreshAssets = serializedObject.FindProperty("AutoRefreshAssets");
            RefreshValidationCache();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawSettingsHeader();
            EditorGUILayout.Space(5);

            EditorGUI.BeginChangeCheck();
            DrawProjectDirectoryField();
            EditorGUILayout.Space(5);
            DrawProjectDirectoryPreview();
            EditorGUILayout.Space(5);
            DrawRunnerFields();
            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                RefreshValidationCache();
                serializedObject.Update();
            }

            EditorGUILayout.Space(10);
            DrawPathPreview();
            EditorGUILayout.Space(10);
            DrawValidation();
            EditorGUILayout.Space(10);
            DrawActions();
            EditorGUILayout.Space(10);
            DrawExtensionFields();

            serializedObject.ApplyModifiedProperties();
        }

        protected virtual void DrawSettingsHeader()
        {
            EditorGUILayout.LabelField("DataTable Settings", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Configures the default DataTable generation workflow. Projects can derive from " +
                "DataTableSettings and override its virtual methods for custom profiles, paths, or CI behavior.",
                MessageType.Info);
        }

        protected virtual void DrawProjectDirectoryField()
        {
            DrawPropertyIfPresent(_dataTableProjectDir, ProjectDirLabel);
        }

        protected virtual void DrawRunnerFields()
        {
            DrawPropertyIfPresent(_scriptName, ScriptNameLabel);
            DrawPropertyIfPresent(_scriptArguments, ScriptArgumentsLabel);
            DrawPropertyIfPresent(_timeoutSeconds, TimeoutLabel);
            DrawPropertyIfPresent(_autoRefreshAssets, RefreshLabel);
        }

        protected virtual void DrawPathPreview()
        {
            var request = GetCachedRequest();
            _showResolvedBuildScript = EditorGUILayout.Foldout(
                _showResolvedBuildScript,
                "Resolved Build Script",
                true);

            if (!_showResolvedBuildScript)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField("Script Path", request.ScriptPath, EditorStyles.miniLabel);
                EditorGUI.indentLevel--;
                return;
            }

            var savedWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 120f;

            EditorGUILayout.LabelField("Project Root", request.ProjectRoot);
            EditorGUILayout.LabelField("Project Dir", request.ProjectDirectory);
            EditorGUILayout.LabelField("Work Dir", request.WorkingDirectory);
            EditorGUILayout.LabelField("Script Path", request.ScriptPath);
            EditorGUILayout.LabelField("Arguments", string.IsNullOrEmpty(request.ScriptArguments) ? "(none)" : request.ScriptArguments);

            EditorGUIUtility.labelWidth = savedWidth;
        }

        protected virtual void DrawProjectDirectoryPreview()
        {
            var request = GetCachedRequest();
            var directoryExists = Directory.Exists(request.WorkingDirectory);
            var projectRootExists = Directory.Exists(request.ProjectRoot);

            EditorGUILayout.LabelField("Project Directory", EditorStyles.boldLabel);

            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField(
                new GUIContent(projectRootExists
                    ? "<color=#5a5>OK Project root: " + request.ProjectRoot + "</color>"
                    : "<color=#c55>Missing project root: " + request.ProjectRoot + "</color>"),
                RichWrapMiniLabel);

            EditorGUILayout.LabelField(
                new GUIContent(directoryExists
                    ? "<color=#5a5>OK DataTable dir: " + request.WorkingDirectory + "</color>"
                    : "<color=#c55>Missing DataTable dir: " + request.WorkingDirectory + "</color>"),
                RichWrapMiniLabel);

            if (!string.IsNullOrEmpty(request.ProjectDirectory))
            {
                EditorGUILayout.LabelField(
                    new GUIContent("<color=#888>Configured dir: " + request.ProjectDirectory + "</color>"),
                    RichWrapMiniLabel);
            }

            EditorGUI.indentLevel--;
        }

        protected virtual void DrawValidation()
        {
            var request = GetCachedRequest();
            EditorGUILayout.LabelField("Validation", EditorStyles.boldLabel);

            if (string.IsNullOrEmpty(_cachedValidationError))
            {
                EditorGUILayout.HelpBox(
                    "Ready. The configured Luban project directory and build script both exist.",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.HelpBox(_cachedValidationError, MessageType.Warning);

            if (Directory.Exists(request.WorkingDirectory) && _cachedScripts.Length > 0)
            {
                EditorGUILayout.LabelField("Scripts Found", EditorStyles.miniBoldLabel);
                for (int i = 0; i < _cachedScripts.Length; i++)
                {
                    EditorGUILayout.LabelField("  " + _cachedScripts[i], RichMiniLabel);
                }
            }
        }

        protected virtual void DrawActions()
        {
            EditorGUILayout.LabelField("Quick Actions", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (DrawButton("Refresh"))
                {
                    RefreshValidationCache();
                }

                if (DrawButton("Reveal Settings Asset"))
                {
                    var path = AssetDatabase.GetAssetPath(target);
                    if (!string.IsNullOrEmpty(path))
                    {
                        EditorUtility.RevealInFinder(path);
                    }
                }
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (DrawButton("Open Project Directory"))
                {
                    OpenProjectDirectory();
                }

                if (DrawButton("Run Luban Build"))
                {
                    var result = DataTableLubanRunner.RunWithResult((DataTableSettings)target);
                    if (!result.Success)
                    {
                        EditorUtility.DisplayDialog("Luban Build Failed", result.ErrorMessage, "OK");
                    }
                }
            }
        }

        /// <summary>
        /// Derived editors can override this to draw project-specific fields after the base UI.
        /// </summary>
        protected virtual void DrawExtensionFields()
        {
        }

        protected virtual DataTableLubanRunRequest CreatePreviewRequest()
        {
            return ((DataTableSettings)target).CreateLubanRunRequest();
        }

        protected void RefreshValidationCache()
        {
            _cachedRequest = CreatePreviewRequest();
            _cachedValidationError = DataTableLubanRunner.ValidateRequest(_cachedRequest);
            _cachedScripts = FindBuildScripts(_cachedRequest.WorkingDirectory);
            _lastTargetHash = ComputeTargetHash();
        }

        protected DataTableLubanRunRequest GetCachedRequest()
        {
            if (_cachedRequest == null || _lastTargetHash != ComputeTargetHash())
            {
                RefreshValidationCache();
            }

            return _cachedRequest;
        }

        private void OpenProjectDirectory()
        {
            var request = GetCachedRequest();
            if (Directory.Exists(request.WorkingDirectory))
            {
                EditorUtility.RevealInFinder(request.WorkingDirectory);
                return;
            }

            EditorUtility.DisplayDialog(
                "Directory Not Found",
                "The directory does not exist:\n" + request.WorkingDirectory,
                "OK");
        }

        private static void DrawPropertyIfPresent(SerializedProperty property, GUIContent label)
        {
            if (property != null)
            {
                EditorGUILayout.PropertyField(property, label);
            }
        }

        private static bool DrawButton(string label)
        {
            var rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            return GUI.Button(rect, label);
        }

        private static string[] FindBuildScripts(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return System.Array.Empty<string>();
            }

            var batFiles = Directory.GetFiles(directory, "*.bat", SearchOption.TopDirectoryOnly);
            var shFiles = Directory.GetFiles(directory, "*.sh", SearchOption.TopDirectoryOnly);
            var scripts = new string[batFiles.Length + shFiles.Length];
            for (int i = 0; i < batFiles.Length; i++)
            {
                scripts[i] = Path.GetFileName(batFiles[i]);
            }

            for (int i = 0; i < shFiles.Length; i++)
            {
                scripts[batFiles.Length + i] = Path.GetFileName(shFiles[i]);
            }

            return scripts;
        }

        private int ComputeTargetHash()
        {
            var settings = (DataTableSettings)target;
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + (settings.DataTableProjectDir == null ? 0 : settings.DataTableProjectDir.GetHashCode());
                hash = hash * 31 + (settings.ScriptName == null ? 0 : settings.ScriptName.GetHashCode());
                hash = hash * 31 + (settings.ScriptArguments == null ? 0 : settings.ScriptArguments.GetHashCode());
                hash = hash * 31 + settings.TimeoutSeconds;
                hash = hash * 31 + (settings.AutoRefreshAssets ? 1 : 0);
                return hash;
            }
        }
    }
}
