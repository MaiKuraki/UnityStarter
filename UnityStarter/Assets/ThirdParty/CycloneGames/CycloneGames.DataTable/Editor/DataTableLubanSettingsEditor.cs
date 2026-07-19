using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Handles = UnityEditor.Handles;

namespace CycloneGames.DataTable.Unity.Editor
{
    [CustomEditor(typeof(DataTableLubanSettings), true)]
    [CanEditMultipleObjects]
    public class DataTableLubanSettingsEditor : UnityEditor.Editor
    {
        private const float SectionSpacing = 8f;
        private const float ButtonWidth = 72f;
        private const float ButtonGap = 4f;

        private SerializedProperty _lubanProjectDir;
        private SerializedProperty _scriptName;
        private SerializedProperty _scriptArguments;
        private SerializedProperty _timeoutSeconds;
        private SerializedProperty _autoRefreshAssets;

        private DataTableLubanRunRequest _cachedRequest;
        private string _cachedValidationError;
        private string[] _cachedScripts = Array.Empty<string>();
        private string[] _cachedSettingsPaths = Array.Empty<string>();
        private SerializedProperty[] _extensionProperties = Array.Empty<SerializedProperty>();
        private string _cachedSettingsAssetLabel;
        private string _cachedSettingsCountLabel;
        private string _cachedTimeoutLabel;
        private bool _cachedProjectRootExists;
        private bool _cachedWorkingDirectoryExists;
        private bool _cachedScriptExists;
        private int _lastTargetHash;

        private bool _settingsFoldout = true;
        private bool _pathsFoldout = true;
        private bool _diagnosticsFoldout = true;
        private bool _actionsFoldout = true;
        private bool _extensionsFoldout = true;

        private static readonly GUIContent ProjectDirLabel = new GUIContent("Luban Project Dir");
        private static readonly GUIContent ScriptNameLabel = new GUIContent("Luban Script Name");
        private static readonly GUIContent ScriptArgumentsLabel = new GUIContent("Luban Script Arguments");
        private static readonly GUIContent TimeoutLabel = new GUIContent("Luban Timeout Seconds");
        private static readonly GUIContent RefreshLabel = new GUIContent("Refresh After Luban Build");
        private static readonly GUIContent BrowseLabel = new GUIContent("Browse");
        private static readonly GUIContent UseLabel = new GUIContent("Use");
        private static readonly Color BuildSettingsHeaderColor = new Color(0.22f, 0.46f, 0.70f);
        private static readonly Color BuildSettingsHeaderCollapsedColor = new Color(0.1584f, 0.3312f, 0.5040f);
        private static readonly Color PathsHeaderColor = new Color(0.28f, 0.55f, 0.48f);
        private static readonly Color PathsHeaderCollapsedColor = new Color(0.2016f, 0.3960f, 0.3456f);
        private static readonly Color ValidationHeaderColor = new Color(0.55f, 0.45f, 0.23f);
        private static readonly Color ValidationHeaderCollapsedColor = new Color(0.3960f, 0.3240f, 0.1656f);
        private static readonly Color ActionsHeaderColor = new Color(0.42f, 0.42f, 0.48f);
        private static readonly Color ActionsHeaderCollapsedColor = new Color(0.3024f, 0.3024f, 0.3456f);
        private static readonly Color ExtensionsHeaderColor = new Color(0.42f, 0.34f, 0.56f);
        private static readonly Color ExtensionsHeaderCollapsedColor = new Color(0.3024f, 0.2448f, 0.4032f);

        protected virtual void OnEnable()
        {
            _lubanProjectDir = serializedObject.FindProperty("LubanProjectDir");
            _scriptName = serializedObject.FindProperty("LubanScriptName");
            _scriptArguments = serializedObject.FindProperty("LubanScriptArguments");
            _timeoutSeconds = serializedObject.FindProperty("LubanTimeoutSeconds");
            _autoRefreshAssets = serializedObject.FindProperty("RefreshAssetsAfterLubanBuild");
            _extensionProperties = FindExtensionProperties();
            _cachedSettingsAssetLabel = serializedObject.isEditingMultipleObjects
                ? targets.Length + " assets selected"
                : AssetDatabase.GetAssetPath(target);
            RefreshValidationCache();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawSettingsHeader();
            EditorGUILayout.Space(SectionSpacing);

            EditorGUI.BeginChangeCheck();
            _settingsFoldout = DataTableInspectorUiUtility.DrawFoldoutHeader(
                "Luban Build Settings",
                _settingsFoldout,
                BuildSettingsHeaderColor,
                BuildSettingsHeaderCollapsedColor);
            if (_settingsFoldout)
            {
                DrawSettingsFields();
            }

            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                RefreshValidationCache();
                serializedObject.Update();
            }

            EditorGUILayout.Space(SectionSpacing);
            DrawPathPreview();
            EditorGUILayout.Space(SectionSpacing);
            DrawDiagnostics();
            EditorGUILayout.Space(SectionSpacing);
            DrawActions();
            EditorGUILayout.Space(SectionSpacing);
            EditorGUI.BeginChangeCheck();
            DrawExtensionFields();
            if (EditorGUI.EndChangeCheck())
            {
                serializedObject.ApplyModifiedProperties();
                RefreshValidationCache();
                serializedObject.Update();
            }

            serializedObject.ApplyModifiedProperties();
        }

        protected virtual void DrawSettingsHeader()
        {
            EditorGUILayout.LabelField("DataTable Luban Settings", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Visible project-level Luban generation settings. Default tooling discovers this asset by type; keep exactly one DataTableLubanSettings asset in the project.",
                MessageType.Info);

            DataTableInspectorUiUtility.DrawStatusRow(
                "Settings Asset",
                string.IsNullOrEmpty(_cachedSettingsAssetLabel) ? "(unsaved)" : _cachedSettingsAssetLabel,
                string.IsNullOrEmpty(_cachedSettingsAssetLabel) ? DataTableStatusKind.Warning : DataTableStatusKind.Info);
            DataTableInspectorUiUtility.DrawStatusRow(
                "Build Status",
                serializedObject.isEditingMultipleObjects
                    ? "Multiple Selection"
                    : string.IsNullOrEmpty(_cachedValidationError) ? "Ready" : "Needs Attention",
                serializedObject.isEditingMultipleObjects
                    ? DataTableStatusKind.Info
                    : string.IsNullOrEmpty(_cachedValidationError) ? DataTableStatusKind.Success : DataTableStatusKind.Warning);
        }

        protected virtual void DrawSettingsFields()
        {
            DrawProjectDirectoryField();
            DrawPropertyIfPresent(_scriptName, ScriptNameLabel);
            DrawScriptSuggestions();
            DrawPropertyIfPresent(_scriptArguments, ScriptArgumentsLabel);
            DrawPropertyIfPresent(_timeoutSeconds, TimeoutLabel);
            DrawPropertyIfPresent(_autoRefreshAssets, RefreshLabel);
        }

        protected virtual void DrawProjectDirectoryField()
        {
            if (_lubanProjectDir == null)
            {
                return;
            }

            var rect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
            var fieldRect = rect;
            fieldRect.width -= ButtonWidth + ButtonGap;

            var buttonRect = rect;
            buttonRect.xMin = fieldRect.xMax + ButtonGap;

            EditorGUI.PropertyField(fieldRect, _lubanProjectDir, ProjectDirLabel);
            using (new EditorGUI.DisabledScope(serializedObject.isEditingMultipleObjects))
            {
                if (GUI.Button(buttonRect, BrowseLabel, EditorStyles.miniButton))
                {
                    BrowseProjectDirectory();
                }
            }
        }

        protected virtual void DrawPathPreview()
        {
            _pathsFoldout = DataTableInspectorUiUtility.DrawFoldoutHeader(
                "Resolved Paths",
                _pathsFoldout,
                PathsHeaderColor,
                PathsHeaderCollapsedColor);
            if (!_pathsFoldout)
            {
                return;
            }

            if (serializedObject.isEditingMultipleObjects)
            {
                EditorGUILayout.HelpBox(
                    "Resolved paths are hidden while multiple settings assets are selected because derived settings can resolve different project roots.",
                    MessageType.Info);
                return;
            }

            var request = GetCachedRequest();
            var savedWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 118f;

            EditorGUILayout.LabelField("Project Root", request.ProjectRoot);
            EditorGUILayout.LabelField("Configured Dir", request.ProjectDirectory);
            EditorGUILayout.LabelField("Working Dir", request.WorkingDirectory);
            EditorGUILayout.LabelField("Script Path", request.ScriptPath);
            EditorGUILayout.LabelField(
                "Arguments",
                string.IsNullOrWhiteSpace(request.ScriptArguments) ? "(none)" : request.ScriptArguments);

            EditorGUIUtility.labelWidth = savedWidth;
        }

        protected virtual void DrawDiagnostics()
        {
            _diagnosticsFoldout = DataTableInspectorUiUtility.DrawFoldoutHeader(
                "Validation",
                _diagnosticsFoldout,
                ValidationHeaderColor,
                ValidationHeaderCollapsedColor);
            if (!_diagnosticsFoldout)
            {
                return;
            }

            if (serializedObject.isEditingMultipleObjects)
            {
                EditorGUILayout.HelpBox(
                    "Select one settings asset to validate resolved paths. Shared serialized fields can still be edited above.",
                    MessageType.Info);
                return;
            }

            var request = GetCachedRequest();

            DataTableInspectorUiUtility.DrawStatusRow(
                "Project Root",
                _cachedProjectRootExists ? request.ProjectRoot : "Missing",
                _cachedProjectRootExists ? DataTableStatusKind.Success : DataTableStatusKind.Error);
            DataTableInspectorUiUtility.DrawStatusRow(
                "Luban Directory",
                _cachedWorkingDirectoryExists ? request.WorkingDirectory : "Missing",
                _cachedWorkingDirectoryExists ? DataTableStatusKind.Success : DataTableStatusKind.Error);
            DataTableInspectorUiUtility.DrawStatusRow(
                "Build Script",
                _cachedScriptExists ? Path.GetFileName(request.ScriptPath) : "Missing",
                _cachedScriptExists ? DataTableStatusKind.Success : DataTableStatusKind.Error);
            DataTableInspectorUiUtility.DrawStatusRow(
                "Settings Count",
                _cachedSettingsCountLabel,
                _cachedSettingsPaths.Length <= 1 ? DataTableStatusKind.Success : DataTableStatusKind.Warning);
            DataTableInspectorUiUtility.DrawStatusRow(
                "Timeout",
                _cachedTimeoutLabel,
                DataTableStatusKind.Info);
            DataTableInspectorUiUtility.DrawStatusRow(
                "Asset Refresh",
                request.AutoRefreshAssets ? "After Success" : "Manual",
                request.AutoRefreshAssets ? DataTableStatusKind.Success : DataTableStatusKind.Info);

            if (_cachedSettingsPaths.Length > 1)
            {
                EditorGUILayout.HelpBox(
                    "Multiple DataTableLubanSettings assets exist. Default tooling uses the first deterministic asset path; remove duplicates to keep generation predictable.",
                    MessageType.Warning);
                for (int i = 0; i < _cachedSettingsPaths.Length; i++)
                {
                    EditorGUILayout.LabelField(_cachedSettingsPaths[i], EditorStyles.miniLabel);
                }
            }

            if (string.IsNullOrEmpty(_cachedValidationError))
            {
                EditorGUILayout.HelpBox(
                    "Ready. The configured Luban project directory and build script both exist.",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.HelpBox(_cachedValidationError, MessageType.Warning);
        }

        protected virtual void DrawActions()
        {
            _actionsFoldout = DataTableInspectorUiUtility.DrawFoldoutHeader(
                "Actions",
                _actionsFoldout,
                ActionsHeaderColor,
                ActionsHeaderCollapsedColor);
            if (!_actionsFoldout)
            {
                return;
            }

            if (serializedObject.isEditingMultipleObjects)
            {
                EditorGUILayout.HelpBox(
                    "Build and filesystem actions require a single settings asset selection.",
                    MessageType.Info);
                return;
            }

            DrawButtonRow("Refresh", RefreshValidationCache, "Reveal Settings", RevealSettingsAsset);
            DrawButtonRow("Open Directory", OpenProjectDirectory, "Validate Paths", RefreshValidationCache);

            if (DataTableLubanRunner.IsRunning)
            {
                EditorGUILayout.HelpBox(
                    "A Luban build is already running. Only the owning caller or editor shutdown can request cancellation.",
                    MessageType.Info);
                return;
            }

            EditorGUILayout.HelpBox(
                "The runner is synchronous and blocks editor interaction while Luban runs, so Inspector cancellation is not available. Shutdown/domain-reload cancellation is best-effort; the positive process timeout is the bounded fallback.",
                MessageType.Info);

            var rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            using (new EditorGUI.DisabledScope(!string.IsNullOrEmpty(_cachedValidationError)))
            {
                if (GUI.Button(rect, "Run Luban Build"))
                {
                    RunLubanBuild();
                }
            }
        }

        /// <summary>
        /// Derived editors can override this to draw project-specific fields after the base UI.
        /// </summary>
        protected virtual void DrawExtensionFields()
        {
            if (_extensionProperties.Length == 0)
            {
                return;
            }

            _extensionsFoldout = DataTableInspectorUiUtility.DrawFoldoutHeader(
                "Project Extensions",
                _extensionsFoldout,
                ExtensionsHeaderColor,
                ExtensionsHeaderCollapsedColor);
            if (!_extensionsFoldout)
            {
                return;
            }

            for (int i = 0; i < _extensionProperties.Length; i++)
            {
                EditorGUILayout.PropertyField(_extensionProperties[i], true);
            }
        }

        protected virtual DataTableLubanRunRequest CreatePreviewRequest()
        {
            return ((DataTableLubanSettings)target).CreateLubanRunRequest();
        }

        protected void RefreshValidationCache()
        {
            _cachedRequest = CreatePreviewRequest();
            _cachedValidationError = DataTableLubanRunner.ValidateRequest(_cachedRequest);
            _cachedScripts = FindBuildScripts(_cachedRequest.WorkingDirectory);
            _cachedSettingsPaths = FindSettingsAssetPaths();
            _cachedSettingsCountLabel = _cachedSettingsPaths.Length <= 1
                ? "Single"
                : _cachedSettingsPaths.Length + " assets found";
            _cachedTimeoutLabel = _cachedRequest.TimeoutMilliseconds + " ms";
            _cachedProjectRootExists = Directory.Exists(_cachedRequest.ProjectRoot);
            _cachedWorkingDirectoryExists = Directory.Exists(_cachedRequest.WorkingDirectory);
            _cachedScriptExists = !string.IsNullOrEmpty(_cachedRequest.ScriptPath) && File.Exists(_cachedRequest.ScriptPath);
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

        private void DrawScriptSuggestions()
        {
            if (serializedObject.isEditingMultipleObjects || _cachedScripts.Length == 0)
            {
                return;
            }

            EditorGUI.indentLevel++;
            EditorGUILayout.LabelField("Scripts Found", EditorStyles.miniBoldLabel);
            for (int i = 0; i < _cachedScripts.Length; i++)
            {
                var scriptName = _cachedScripts[i];
                var rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
                var labelRect = rect;
                labelRect.width -= ButtonWidth + ButtonGap;

                var buttonRect = rect;
                buttonRect.xMin = labelRect.xMax + ButtonGap;

                EditorGUI.LabelField(labelRect, scriptName, EditorStyles.miniLabel);
                if (GUI.Button(buttonRect, UseLabel, EditorStyles.miniButton))
                {
                    _scriptName.stringValue = Path.GetFileNameWithoutExtension(scriptName);
                    serializedObject.ApplyModifiedProperties();
                    RefreshValidationCache();
                }
            }

            EditorGUI.indentLevel--;
        }

        private void BrowseProjectDirectory()
        {
            var request = GetCachedRequest();
            var startDirectory = Directory.Exists(request.WorkingDirectory)
                ? request.WorkingDirectory
                : Directory.Exists(request.ProjectRoot) ? request.ProjectRoot : Application.dataPath;
            var selected = EditorUtility.OpenFolderPanel("Select Luban Project Directory", startDirectory, string.Empty);
            if (string.IsNullOrEmpty(selected))
            {
                return;
            }

            _lubanProjectDir.stringValue = MakeProjectRelativePath(request.ProjectRoot, selected);
            serializedObject.ApplyModifiedProperties();
            RefreshValidationCache();
        }

        private void RevealSettingsAsset()
        {
            var path = AssetDatabase.GetAssetPath(target);
            if (!string.IsNullOrEmpty(path))
            {
                EditorUtility.RevealInFinder(path);
            }
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

        private void RunLubanBuild()
        {
            var result = DataTableLubanRunner.RunWithResult((DataTableLubanSettings)target);
            if (!result.Success)
            {
                EditorUtility.DisplayDialog(
                    "Luban Build Failed",
                    DataTableLubanRunner.BuildFailureDialogMessage(result),
                    "OK");
            }

            RefreshValidationCache();
        }

        private static void DrawPropertyIfPresent(SerializedProperty property, GUIContent label)
        {
            if (property != null)
            {
                EditorGUILayout.PropertyField(property, label);
            }
        }

        private SerializedProperty[] FindExtensionProperties()
        {
            var properties = new System.Collections.Generic.List<SerializedProperty>();
            var iterator = serializedObject.GetIterator();
            var enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (IsBaseProperty(iterator.propertyPath))
                {
                    continue;
                }

                properties.Add(iterator.Copy());
            }

            return properties.ToArray();
        }

        private static bool IsBaseProperty(string propertyPath)
        {
            return propertyPath == "m_Script" ||
                   propertyPath == "LubanProjectDir" ||
                   propertyPath == "LubanScriptName" ||
                   propertyPath == "LubanScriptArguments" ||
                   propertyPath == "LubanTimeoutSeconds" ||
                   propertyPath == "RefreshAssetsAfterLubanBuild";
        }

        private static void DrawButtonRow(
            string leftLabel,
            Action leftAction,
            string rightLabel,
            Action rightAction)
        {
            var rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            var leftRect = rect;
            leftRect.width = (rect.width - ButtonGap) * 0.5f;

            var rightRect = rect;
            rightRect.xMin = leftRect.xMax + ButtonGap;

            if (GUI.Button(leftRect, leftLabel))
            {
                leftAction();
            }

            if (GUI.Button(rightRect, rightLabel))
            {
                rightAction();
            }
        }

        private static string[] FindBuildScripts(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return Array.Empty<string>();
            }

            try
            {
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

                Array.Sort(scripts, StringComparer.OrdinalIgnoreCase);
                return scripts;
            }
            catch (IOException)
            {
                return Array.Empty<string>();
            }
            catch (UnauthorizedAccessException)
            {
                return Array.Empty<string>();
            }
        }

        private static string[] FindSettingsAssetPaths()
        {
            var guids = AssetDatabase.FindAssets("t:DataTableLubanSettings");
            if (guids.Length == 0)
            {
                return Array.Empty<string>();
            }

            var paths = new string[guids.Length];
            for (int i = 0; i < guids.Length; i++)
            {
                paths[i] = AssetDatabase.GUIDToAssetPath(guids[i]);
            }

            Array.Sort(paths, StringComparer.Ordinal);
            return paths;
        }

        private static string MakeProjectRelativePath(string projectRoot, string selectedPath)
        {
            try
            {
                var root = EnsureTrailingSeparator(Path.GetFullPath(projectRoot));
                var fullPath = Path.GetFullPath(selectedPath);
                var comparison = Path.DirectorySeparatorChar == '\\'
                    ? StringComparison.OrdinalIgnoreCase
                    : StringComparison.Ordinal;

                if (fullPath.StartsWith(root, comparison))
                {
                    var relative = fullPath.Substring(root.Length);
                    return string.IsNullOrEmpty(relative) ? "." : relative.Replace('\\', '/');
                }

                return fullPath.Replace('\\', '/');
            }
            catch (ArgumentException)
            {
                return selectedPath.Replace('\\', '/');
            }
            catch (NotSupportedException)
            {
                return selectedPath.Replace('\\', '/');
            }
        }

        private static string EnsureTrailingSeparator(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return string.Empty;
            }

            var last = path[path.Length - 1];
            if (last == Path.DirectorySeparatorChar || last == Path.AltDirectorySeparatorChar)
            {
                return path;
            }

            return path + Path.DirectorySeparatorChar;
        }

        private int ComputeTargetHash()
        {
            var settings = (DataTableLubanSettings)target;
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + (settings.LubanProjectDir == null ? 0 : settings.LubanProjectDir.GetHashCode());
                hash = hash * 31 + (settings.LubanScriptName == null ? 0 : settings.LubanScriptName.GetHashCode());
                hash = hash * 31 + (settings.LubanScriptArguments == null ? 0 : settings.LubanScriptArguments.GetHashCode());
                hash = hash * 31 + settings.LubanTimeoutSeconds;
                hash = hash * 31 + (settings.RefreshAssetsAfterLubanBuild ? 1 : 0);
                return hash;
            }
        }
    }

    internal enum DataTableStatusKind
    {
        Info,
        Success,
        Warning,
        Error
    }

    internal static class DataTableInspectorUiUtility
    {
        private const float HeaderHorizontalPadding = 4f;
        private const float HeaderArrowWidth = 13f;
        private const float StatusBadgeWidth = 70f;
        private const float StatusHeight = 19f;

        private static readonly Vector3[] TrianglePoints = new Vector3[3];
        private static readonly Color FoldoutBorderColor = new Color(0f, 0f, 0f, 0.20f);
        private static readonly Color StatusInfoColor = new Color(0.35f, 0.42f, 0.50f);
        private static readonly Color StatusSuccessColor = new Color(0.24f, 0.55f, 0.30f);
        private static readonly Color StatusWarningColor = new Color(0.70f, 0.48f, 0.18f);
        private static readonly Color StatusErrorColor = new Color(0.66f, 0.25f, 0.22f);
        private static readonly Color FoldoutTriangleColor = new Color(0.90f, 0.90f, 0.90f, 0.95f);

        private static GUIStyle _foldoutLabelStyle;
        private static GUIStyle _statusLabelStyle;
        private static GUIStyle _statusNameStyle;
        private static GUIStyle _statusValueStyle;

        public static bool DrawFoldoutHeader(string title, bool foldout, Color expandedColor, Color collapsedColor)
        {
            EnsureStyles();

            var rect = EditorGUILayout.GetControlRect(false, 22f);
            var backgroundColor = foldout ? expandedColor : collapsedColor;
            EditorGUI.DrawRect(rect, backgroundColor);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), FoldoutBorderColor);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), FoldoutBorderColor);

            var arrowRect = new Rect(
                rect.x + HeaderHorizontalPadding,
                rect.y + 2f,
                HeaderArrowWidth,
                rect.height - 4f);

            var labelRect = new Rect(
                arrowRect.xMax + 1f,
                rect.y,
                rect.width - (arrowRect.xMax - rect.x) - HeaderHorizontalPadding - 1f,
                rect.height);

            DrawFoldoutTriangle(arrowRect, foldout);
            EditorGUI.LabelField(labelRect, title, _foldoutLabelStyle);

            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                foldout = !foldout;
                Event.current.Use();
            }

            return foldout;
        }

        public static void DrawStatusRow(string label, string value, DataTableStatusKind statusKind)
        {
            EnsureStyles();

            var rect = EditorGUILayout.GetControlRect(false, StatusHeight);
            var badgeRect = new Rect(rect.x, rect.y + 2f, StatusBadgeWidth, rect.height - 4f);
            var nameRect = new Rect(
                badgeRect.xMax + 6f,
                rect.y,
                104f,
                rect.height);
            var valueRect = new Rect(
                nameRect.xMax + 4f,
                rect.y,
                Mathf.Max(0f, rect.xMax - nameRect.xMax - 4f),
                rect.height);

            EditorGUI.DrawRect(badgeRect, GetStatusColor(statusKind));
            EditorGUI.LabelField(badgeRect, GetStatusText(statusKind), _statusLabelStyle);
            EditorGUI.LabelField(nameRect, label, _statusNameStyle);
            EditorGUI.LabelField(valueRect, value, _statusValueStyle);
        }

        private static void EnsureStyles()
        {
            if (_foldoutLabelStyle != null)
            {
                return;
            }

            _foldoutLabelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = Color.white },
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };

            _statusLabelStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                normal = { textColor = Color.white },
                alignment = TextAnchor.MiddleCenter,
                fontSize = 10
            };

            _statusNameStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontStyle = FontStyle.Bold,
                wordWrap = false,
                alignment = TextAnchor.MiddleLeft
            };

            _statusValueStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                wordWrap = false,
                alignment = TextAnchor.MiddleLeft
            };
        }

        private static Color GetStatusColor(DataTableStatusKind statusKind)
        {
            switch (statusKind)
            {
                case DataTableStatusKind.Success:
                    return StatusSuccessColor;
                case DataTableStatusKind.Warning:
                    return StatusWarningColor;
                case DataTableStatusKind.Error:
                    return StatusErrorColor;
                default:
                    return StatusInfoColor;
            }
        }

        private static string GetStatusText(DataTableStatusKind statusKind)
        {
            switch (statusKind)
            {
                case DataTableStatusKind.Success:
                    return "OK";
                case DataTableStatusKind.Warning:
                    return "WARN";
                case DataTableStatusKind.Error:
                    return "ERROR";
                default:
                    return "INFO";
            }
        }

        private static void DrawFoldoutTriangle(Rect rect, bool expanded)
        {
            var center = rect.center;
            if (expanded)
            {
                TrianglePoints[0] = new Vector3(center.x - 4f, center.y - 2f);
                TrianglePoints[1] = new Vector3(center.x + 4f, center.y - 2f);
                TrianglePoints[2] = new Vector3(center.x, center.y + 3f);
            }
            else
            {
                TrianglePoints[0] = new Vector3(center.x - 2f, center.y - 4f);
                TrianglePoints[1] = new Vector3(center.x - 2f, center.y + 4f);
                TrianglePoints[2] = new Vector3(center.x + 3f, center.y);
            }

            Handles.BeginGUI();
            var previousColor = Handles.color;
            Handles.color = FoldoutTriangleColor;
            Handles.DrawAAConvexPolygon(TrianglePoints);
            Handles.color = previousColor;
            Handles.EndGUI();
        }
    }
}
