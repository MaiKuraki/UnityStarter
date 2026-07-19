using System;
using System.Buffers;
using System.IO;
using System.Text;
using CycloneGames.IO;
using CycloneGames.InputSystem.Runtime;
using UnityEditor;
using UnityEngine;
using VYaml.Emitter;
using VYaml.Serialization;

namespace CycloneGames.InputSystem.Editor
{
    public partial class InputEditorWindow : EditorWindow
    {
        private const int MaxConfigBytes = FileInputConfigurationStore.DefaultMaximumBytes;
        private const string DefaultConfigFileName = "input_config.yaml";
        private const string UserConfigFileName = "user_input_settings.yaml";
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private static readonly Color SourceColor = new Color(0.20f, 0.48f, 0.78f);
        private static readonly Color OutputColor = new Color(0.10f, 0.58f, 0.68f);
        private static readonly Color EditableColor = new Color(0.18f, 0.62f, 0.38f);
        private static readonly Color WarningColor = new Color(0.82f, 0.51f, 0.12f);
        private static readonly Color InvalidColor = new Color(0.76f, 0.25f, 0.22f);
        private static readonly Color NeutralColor = new Color(0.38f, 0.42f, 0.47f);

        private static readonly GUIContent RootJoinActionLabel =
            new GUIContent("Root Join Action", "Optional join action shared by every player slot.");
        private static readonly GUIContent PlayerSlotsLabel = new GUIContent("Player Slots");
        private static readonly GUIContent AddPlayerLabel = new GUIContent("Add Player");
        private static readonly GUIContent DefaultConfigFolderLabel = new GUIContent("Default Config Folder");
        private static readonly GUIContent UserConfigSubdirectoryLabel = new GUIContent(
            "User Config Subdirectory",
            "A relative directory under Application.persistentDataPath.");
        private static readonly GUIContent CodegenFolderLabel = new GUIContent("Codegen Output Folder");
        private static readonly GUIContent NamespaceLabel = new GUIContent("Namespace");

        private InputConfigurationSO _configSO;
        private SerializedObject _serializedConfig;
        private Vector2 _scrollPosition;
        private Vector2 _settingsScrollPosition;
        private GUIStyle _headerTitleStyle;
        private GUIStyle _headerSubtitleStyle;
        private GUIStyle _sectionTitleStyle;
        private GUIStyle _cardTitleStyle;
        private GUIStyle _badgeStyle;
        private GUIStyle _pathLabelStyle;
        private GUIStyle _statusBodyStyle;
        private bool _validationCacheDirty = true;
        private string _validationMessage;
        private MessageType _validationMessageType = MessageType.None;

        private string _statusMessage;
        private MessageType _statusMessageType = MessageType.Info;

        private DefaultAsset _defaultConfigFolder;
        private string _defaultConfigFolderPath;
        private string _defaultConfigAssetPath;
        private string _defaultConfigPath;
        private string _defaultConfigFullPathDisplay;

        private string _userConfigSubPath;
        private string _userConfigPath;
        private string _userConfigFullPathDisplay;

        private DefaultAsset _codegenFolder;
        private string _codegenPath;
        private string _codegenNamespace;

        [MenuItem("Tools/CycloneGames/Input System Editor")]
        public static void ShowWindow()
        {
            GetWindow<InputEditorWindow>("Input System Editor");
        }

        private void OnEnable()
        {
            minSize = new Vector2(900f, 620f);
            LoadEditorSettings();
            Undo.undoRedoPerformed += HandleUndoRedo;
            Undo.postprocessModifications += HandlePostprocessModifications;
            AssemblyReloadEvents.beforeAssemblyReload += DestroyWorkingCopy;
            LoadUserConfig();
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= HandleUndoRedo;
            Undo.postprocessModifications -= HandlePostprocessModifications;
            AssemblyReloadEvents.beforeAssemblyReload -= DestroyWorkingCopy;
            DestroyWorkingCopy();
        }

        private void LoadEditorSettings()
        {
            InputEditorSettings settings = InputEditorSettings.instance;
            _userConfigSubPath = settings.UserConfigSubdirectory.Trim();
            _codegenPath = settings.CodegenFolder;
            _codegenNamespace = settings.GeneratedNamespace;
            _defaultConfigFolderPath = settings.DefaultConfigFolder;

            _codegenFolder = LoadValidFolder(_codegenPath, "Assets");
            _codegenPath = AssetDatabase.GetAssetPath(_codegenFolder);
            _defaultConfigFolder = LoadValidFolder(_defaultConfigFolderPath, "Assets/StreamingAssets");
            _defaultConfigFolderPath = AssetDatabase.GetAssetPath(_defaultConfigFolder);

            UpdateUserConfigPath();
            UpdateDefaultConfigPath();
        }

        private static DefaultAsset LoadValidFolder(string requestedPath, string fallbackPath)
        {
            if (!string.IsNullOrWhiteSpace(requestedPath) && AssetDatabase.IsValidFolder(requestedPath))
            {
                return AssetDatabase.LoadAssetAtPath<DefaultAsset>(requestedPath);
            }

            string resolvedFallback = AssetDatabase.IsValidFolder(fallbackPath) ? fallbackPath : "Assets";
            return AssetDatabase.LoadAssetAtPath<DefaultAsset>(resolvedFallback);
        }

        private bool UpdateUserConfigPath()
        {
            if (!InputEditorFileUtility.TryResolveUserConfigPath(
                    Application.persistentDataPath,
                    _userConfigSubPath,
                    UserConfigFileName,
                    out _userConfigPath,
                    out string error))
            {
                _userConfigPath = null;
                _userConfigFullPathDisplay = error;
                SetStatus(error, MessageType.Error);
                return false;
            }

            _userConfigFullPathDisplay = _userConfigPath.Replace('\\', '/');
            return true;
        }

        private bool UpdateDefaultConfigPath()
        {
            if (!InputEditorFileUtility.TryResolveAssetFile(
                    _defaultConfigFolder,
                    DefaultConfigFileName,
                    out _defaultConfigAssetPath,
                    out _defaultConfigPath,
                    out string error))
            {
                _defaultConfigAssetPath = null;
                _defaultConfigPath = null;
                _defaultConfigFullPathDisplay = error;
                SetStatus(error, MessageType.Error);
                return false;
            }

            _defaultConfigFullPathDisplay = _defaultConfigPath.Replace('\\', '/');
            return true;
        }

        private void HandleUndoRedo()
        {
            _serializedConfig?.UpdateIfRequiredOrScript();
            MarkValidationDirty();
        }

        private UndoPropertyModification[] HandlePostprocessModifications(
            UndoPropertyModification[] modifications)
        {
            if (_configSO == null || modifications == null) return modifications;

            for (int index = 0; index < modifications.Length; index++)
            {
                PropertyModification current = modifications[index].currentValue;
                PropertyModification previous = modifications[index].previousValue;
                if ((current != null && current.target == _configSO) ||
                    (previous != null && previous.target == _configSO))
                {
                    MarkValidationDirty();
                    break;
                }
            }
            return modifications;
        }

        private void OnGUI()
        {
            EnsureStyles();
            DrawHeader();
            DrawToolbar();
            DrawStatusBar();

            EditorGUILayout.BeginHorizontal();
            DrawSettings();
            DrawWorkspace();
            EditorGUILayout.EndHorizontal();

            if (_validationCacheDirty && Event.current.type == EventType.Repaint)
            {
                RebuildValidationCache();
                _validationCacheDirty = false;
            }
        }

        private void EnsureStyles()
        {
            if (_headerTitleStyle != null) return;

            _headerTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 18,
                normal = { textColor = Color.white }
            };
            _headerSubtitleStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 11,
                normal = { textColor = new Color(0.82f, 0.88f, 0.96f) }
            };
            _sectionTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                margin = new RectOffset(0, 0, 8, 4),
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = Color.white }
            };
            _cardTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13
            };
            _badgeStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                clipping = TextClipping.Clip,
                normal = { textColor = Color.white }
            };
            _pathLabelStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                margin = new RectOffset(2, 0, 4, 1)
            };
            _statusBodyStyle = new GUIStyle(EditorStyles.wordWrappedMiniLabel)
            {
                alignment = TextAnchor.MiddleLeft
            };
        }

        private void DrawHeader()
        {
            Rect header = GUILayoutUtility.GetRect(0f, 62f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(header, EditorGUIUtility.isProSkin
                ? new Color(0.10f, 0.18f, 0.29f)
                : new Color(0.16f, 0.34f, 0.55f));
            GUI.Label(new Rect(header.x + 16f, header.y + 9f, header.width - 32f, 24f),
                "Input System Editor", _headerTitleStyle);
            GUI.Label(new Rect(header.x + 17f, header.y + 34f, header.width - 34f, 18f),
                "Author validated runtime YAML and deterministic action constants.", _headerSubtitleStyle);
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar, GUILayout.Height(22f));
            DrawToolbarGroupLabel("SOURCE", SourceColor);
            if (GUILayout.Button("Load User", EditorStyles.toolbarButton, GUILayout.Height(20f)))
            {
                LoadUserConfig();
            }
            if (GUILayout.Button("Load Default", EditorStyles.toolbarButton, GUILayout.Height(20f)))
            {
                LoadDefaultConfig();
            }
            if (GUILayout.Button("Generate Default", EditorStyles.toolbarButton, GUILayout.Height(20f)))
            {
                GenerateDefaultConfigFile();
            }
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar, GUILayout.Height(22f));
            DrawToolbarGroupLabel("OUTPUT", OutputColor);
            using (new EditorGUI.DisabledScope(_configSO == null))
            {
                if (GUILayout.Button("Save User Config", EditorStyles.toolbarButton, GUILayout.Height(20f)))
                {
                    SaveChangesToUserConfig();
                }
                if (GUILayout.Button("Save User + Generate Code", EditorStyles.toolbarButton, GUILayout.Height(20f)))
                {
                    SaveChangesToUserConfig(true);
                }
                if (GUILayout.Button("Save Project Default", EditorStyles.toolbarButton, GUILayout.Height(20f)))
                {
                    OverrideDefaultConfig();
                }
                if (GUILayout.Button("Restore User from Default", EditorStyles.toolbarButton, GUILayout.Height(20f)))
                {
                    ResetToDefault();
                }
                if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Height(20f)))
                {
                    ClearEditor();
                    SetStatus("Cleared the in-memory editor configuration.", MessageType.Info);
                }
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawToolbarGroupLabel(string label, Color color)
        {
            Rect rect = GUILayoutUtility.GetRect(64f, 20f, GUILayout.Width(64f), GUILayout.Height(20f));
            rect.y += 1f;
            rect.height -= 2f;
            DrawBadge(rect, label, color);
        }

        private void DrawStatusBar()
        {
            if (!string.IsNullOrEmpty(_statusMessage))
            {
                DrawFeedbackCard("LAST ACTION", _statusMessage, _statusMessageType);
            }
            if (!string.IsNullOrEmpty(_validationMessage))
            {
                DrawFeedbackCard(
                    _validationMessageType == MessageType.Error ? "VALIDATION BLOCKED" : "VALIDATION REVIEW",
                    _validationMessage +
                    (_validationMessageType == MessageType.Error
                        ? " Editing remains enabled; fix the field below before saving."
                        : string.Empty),
                    _validationMessageType);
            }
        }

        private void DrawFeedbackCard(string title, string message, MessageType type)
        {
            Color accent = GetMessageColor(type);
            Rect rect = EditorGUILayout.GetControlRect(
                false,
                Mathf.Max(38f, _statusBodyStyle.CalcHeight(new GUIContent(message), position.width - 170f) + 14f));
            Color panel = EditorGUIUtility.isProSkin
                ? new Color(0.17f, 0.18f, 0.20f)
                : new Color(0.84f, 0.86f, 0.89f);
            EditorGUI.DrawRect(rect, panel);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 5f, rect.height), accent);
            DrawStatusTag(new Rect(rect.x + 15f, rect.y + 8f, 126f, 19f), title, accent);
            EditorGUI.LabelField(
                new Rect(rect.x + 150f, rect.y + 5f, rect.width - 160f, rect.height - 10f),
                new GUIContent(message, message),
                _statusBodyStyle);
            EditorGUILayout.Space(3f);
        }

        private void DrawSettings()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(310f), GUILayout.ExpandHeight(true));
            _settingsScrollPosition = EditorGUILayout.BeginScrollView(_settingsScrollPosition);
            EditorGUILayout.LabelField("Project Settings", _cardTitleStyle);
            EditorGUILayout.LabelField(
                "Paths are explicit, reviewable, and stored per project user.",
                EditorStyles.wordWrappedMiniLabel);

            DrawSectionHeader("Runtime Files", SourceColor);

            DefaultAsset selectedDefaultFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                DefaultConfigFolderLabel,
                _defaultConfigFolder,
                typeof(DefaultAsset),
                false);
            if (selectedDefaultFolder != _defaultConfigFolder)
            {
                TrySetDefaultConfigFolder(selectedDefaultFolder);
            }

            if (!AssetDatabase.IsValidFolder("Assets/StreamingAssets"))
            {
                EditorGUILayout.HelpBox(
                    "The optional StreamingAssets export target does not exist. Create it only when the product selects that runtime adapter.",
                    MessageType.Info);
                if (GUILayout.Button("Create StreamingAssets Folder"))
                {
                    AssetDatabase.CreateFolder("Assets", "StreamingAssets");
                    TrySetDefaultConfigFolder(
                        AssetDatabase.LoadAssetAtPath<DefaultAsset>("Assets/StreamingAssets"));
                }
            }

            DrawReadOnlyPathField("Default Config Path", _defaultConfigFullPathDisplay);
            DrawPathActions(_defaultConfigPath, _defaultConfigAssetPath, "Reveal Default", "Ping Asset");

            string requestedSubdirectory = EditorGUILayout.DelayedTextField(
                UserConfigSubdirectoryLabel,
                _userConfigSubPath ?? string.Empty);
            if (!string.Equals(requestedSubdirectory, _userConfigSubPath, StringComparison.Ordinal))
            {
                TrySetUserConfigSubdirectory(requestedSubdirectory);
            }

            DrawReadOnlyPathField("User Config Path", _userConfigFullPathDisplay);
            DrawReadOnlyPathField("PersistentData Root", Application.persistentDataPath);
            DrawPathActions(_userConfigPath, null, "Reveal User", null);

            DrawSectionHeader("Code Generation", OutputColor);

            DefaultAsset selectedCodegenFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                CodegenFolderLabel,
                _codegenFolder,
                typeof(DefaultAsset),
                false);
            if (selectedCodegenFolder != _codegenFolder)
            {
                TrySetCodegenFolder(selectedCodegenFolder);
            }

            string requestedNamespace = EditorGUILayout.DelayedTextField(
                NamespaceLabel,
                _codegenNamespace ?? string.Empty);
            if (!string.Equals(requestedNamespace, _codegenNamespace, StringComparison.Ordinal))
            {
                _codegenNamespace = requestedNamespace;
                InputEditorSettings.instance.GeneratedNamespace = requestedNamespace;
                InputEditorSettings.instance.SaveSettings();
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.HelpBox(
                "Generated action IDs are signed FNV-1a 32-bit hashes of context/map/action. " +
                "Regenerate after changing any of those names.",
                MessageType.Info);
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawWorkspace()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            Rect header = EditorGUILayout.GetControlRect(false, 24f);
            EditorGUI.LabelField(
                new Rect(header.x, header.y, Mathf.Max(80f, header.width - 230f), header.height),
                "Configuration",
                _cardTitleStyle);
            string validationLabel;
            Color validationColor;
            GetValidationBadge(out validationLabel, out validationColor);
            DrawStatusTag(
                new Rect(header.xMax - 218f, header.y + 2f, 100f, 19f),
                _configSO == null ? "NO CONFIG" : "EDITABLE",
                _configSO == null ? NeutralColor : EditableColor);
            DrawStatusTag(
                new Rect(header.xMax - 110f, header.y + 2f, 108f, 19f),
                validationLabel,
                validationColor);
            EditorGUILayout.LabelField(
                _configSO == null
                    ? "Load the project default, load a user override, or create a validated template."
                    : "Fields remain editable while validation is blocked. Save actions require a valid configuration.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();

            if (_configSO == null || _serializedConfig == null)
            {
                GUILayout.FlexibleSpace();
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Label("No configuration loaded", _cardTitleStyle);
                GUILayout.Label(
                    "No project default is required. Export to StreamingAssets only when the product selects that adapter.",
                    EditorStyles.wordWrappedLabel);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Load Default", GUILayout.Height(30f))) LoadDefaultConfig();
                if (GUILayout.Button("Generate Default", GUILayout.Height(30f))) GenerateDefaultConfigFile();
                EditorGUILayout.EndHorizontal();
                EditorGUILayout.EndVertical();
                GUILayout.FlexibleSpace();
                EditorGUILayout.EndVertical();
                return;
            }

            _serializedConfig.UpdateIfRequiredOrScript();
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            EditorGUI.BeginChangeCheck();
            DrawConfiguration();
            bool controlsChanged = EditorGUI.EndChangeCheck();
            EditorGUILayout.EndScrollView();
            bool propertiesApplied = _serializedConfig.ApplyModifiedProperties();
            if (controlsChanged || propertiesApplied) MarkValidationDirty();
            EditorGUILayout.EndVertical();
        }

        private void DrawSectionHeader(string title, Color color)
        {
            EditorGUILayout.Space(5f);
            Rect rect = EditorGUILayout.GetControlRect(false, 24f);
            Color fill = EditorGUIUtility.isProSkin
                ? new Color(color.r * 0.66f, color.g * 0.66f, color.b * 0.66f, 0.96f)
                : color;
            EditorGUI.DrawRect(rect, fill);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), new Color(0f, 0f, 0f, 0.22f));
            EditorGUI.LabelField(
                new Rect(rect.x + 9f, rect.y + 2f, rect.width - 18f, 20f),
                title,
                _sectionTitleStyle);
        }

        private void DrawReadOnlyPathField(string label, string path)
        {
            string value = path ?? string.Empty;
            GUIContent content = new GUIContent(label, string.IsNullOrEmpty(value) ? "No path resolved." : value);
            EditorGUILayout.LabelField(content, _pathLabelStyle);
            Rect rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUI.TextField(rect, GUIContent.none, value);
            }
            GUI.Label(rect, new GUIContent(string.Empty, content.tooltip), GUIStyle.none);
        }

        private void GetValidationBadge(out string label, out Color color)
        {
            if (_configSO == null)
            {
                label = "WAITING";
                color = NeutralColor;
            }
            else if (_validationMessageType == MessageType.Error)
            {
                label = "INVALID";
                color = InvalidColor;
            }
            else if (_validationMessageType == MessageType.Warning)
            {
                label = "REVIEW";
                color = WarningColor;
            }
            else
            {
                label = "VALID";
                color = EditableColor;
            }
        }

        private void DrawBadge(Rect rect, string label, Color color)
        {
            EditorGUI.DrawRect(rect, new Color(color.r, color.g, color.b, 0.94f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), new Color(1f, 1f, 1f, 0.12f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), new Color(0f, 0f, 0f, 0.22f));
            EditorGUI.LabelField(rect, label, _badgeStyle);
        }

        private void DrawStatusTag(Rect rect, string label, Color color)
        {
            Rect marker = new Rect(rect.x + 4f, rect.y + (rect.height - 8f) * 0.5f, 8f, 8f);
            EditorGUI.DrawRect(marker, color);
            Color previous = GUI.color;
            GUI.color = color;
            EditorGUI.LabelField(
                new Rect(marker.xMax + 7f, rect.y, rect.width - 19f, rect.height),
                label,
                EditorStyles.miniBoldLabel);
            GUI.color = previous;
        }

        private static Color GetMessageColor(MessageType type)
        {
            switch (type)
            {
                case MessageType.Error:
                    return InvalidColor;
                case MessageType.Warning:
                    return WarningColor;
                case MessageType.Info:
                    return SourceColor;
                default:
                    return NeutralColor;
            }
        }

        private static void DrawPathActions(
            string fullPath,
            string assetPath,
            string revealLabel,
            string pingLabel)
        {
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(fullPath)))
            {
                if (GUILayout.Button(revealLabel, EditorStyles.miniButton))
                {
                    string target = File.Exists(fullPath) || Directory.Exists(fullPath)
                        ? fullPath
                        : Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrEmpty(target)) EditorUtility.RevealInFinder(target);
                }
            }
            if (!string.IsNullOrEmpty(pingLabel))
            {
                UnityEngine.Object asset = string.IsNullOrEmpty(assetPath)
                    ? null
                    : AssetDatabase.LoadMainAssetAtPath(assetPath);
                using (new EditorGUI.DisabledScope(asset == null))
                {
                    if (GUILayout.Button(pingLabel, EditorStyles.miniButton))
                    {
                        EditorGUIUtility.PingObject(asset);
                    }
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void TrySetDefaultConfigFolder(DefaultAsset selectedFolder)
        {
            DefaultAsset candidate = selectedFolder ?? LoadValidFolder("Assets/StreamingAssets", "Assets");
            string assetPath = AssetDatabase.GetAssetPath(candidate);
            if (!AssetDatabase.IsValidFolder(assetPath))
            {
                SetStatus("Select a project folder under Assets for the default configuration.", MessageType.Error);
                return;
            }

            _defaultConfigFolder = candidate;
            _defaultConfigFolderPath = assetPath;
            if (UpdateDefaultConfigPath())
            {
                InputEditorSettings.instance.DefaultConfigFolder = assetPath;
                InputEditorSettings.instance.SaveSettings();
                SetStatus("Updated the future default-config output location. Existing files were not moved.", MessageType.Info);
            }
        }

        private void TrySetUserConfigSubdirectory(string requestedSubdirectory)
        {
            string normalizedSubdirectory = (requestedSubdirectory ?? string.Empty).Trim();
            if (!InputEditorFileUtility.TryResolveUserConfigPath(
                    Application.persistentDataPath,
                    normalizedSubdirectory,
                    UserConfigFileName,
                    out string resolvedPath,
                    out string error))
            {
                SetStatus(error, MessageType.Error);
                return;
            }

            _userConfigSubPath = normalizedSubdirectory;
            _userConfigPath = resolvedPath;
            _userConfigFullPathDisplay = resolvedPath.Replace('\\', '/');
            InputEditorSettings.instance.UserConfigSubdirectory = _userConfigSubPath;
            InputEditorSettings.instance.SaveSettings();
        }

        private void TrySetCodegenFolder(DefaultAsset selectedFolder)
        {
            DefaultAsset candidate = selectedFolder ?? LoadValidFolder("Assets", "Assets");
            string assetPath = AssetDatabase.GetAssetPath(candidate);
            if (!AssetDatabase.IsValidFolder(assetPath))
            {
                SetStatus("Select a project folder under Assets for generated code.", MessageType.Error);
                return;
            }

            _codegenFolder = candidate;
            _codegenPath = assetPath;
            InputEditorSettings.instance.CodegenFolder = assetPath;
            InputEditorSettings.instance.SaveSettings();
        }

        private void DrawConfiguration()
        {
            SerializedProperty hasRootJoinAction = _serializedConfig.FindProperty("_hasJoinAction");
            SerializedProperty rootJoinAction = _serializedConfig.FindProperty("_joinAction");
            if (hasRootJoinAction != null)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                Rect sharedAccent = GUILayoutUtility.GetRect(1f, 3f, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(sharedAccent, SourceColor);
                EditorGUILayout.LabelField("Shared Join", _sectionTitleStyle);
                EditorGUILayout.PropertyField(hasRootJoinAction, RootJoinActionLabel);
                if (hasRootJoinAction.boolValue && rootJoinAction != null)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(rootJoinAction, true);
                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.EndVertical();
            }

            SerializedProperty slots = _serializedConfig.FindProperty("_playerSlots");
            if (slots == null || !slots.isArray)
            {
                EditorGUILayout.HelpBox("The working copy has no serializable player-slots field.", MessageType.Error);
                return;
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(PlayerSlotsLabel, EditorStyles.boldLabel);

            for (int slotIndex = 0; slotIndex < slots.arraySize; slotIndex++)
            {
                SerializedProperty slot = slots.GetArrayElementAtIndex(slotIndex);
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                Rect playerAccent = GUILayoutUtility.GetRect(1f, 3f, GUILayout.ExpandWidth(true));
                EditorGUI.DrawRect(playerAccent, OutputColor);
                EditorGUILayout.BeginHorizontal();
                SerializedProperty playerId = slot.FindPropertyRelative("PlayerId");
                string slotTitle = playerId == null ? $"Player Slot {slotIndex}" : $"Player {playerId.intValue}";
                slot.isExpanded = EditorGUILayout.Foldout(
                    slot.isExpanded,
                    slotTitle,
                    true);
                if (GUILayout.Button("Remove", GUILayout.Width(70f)))
                {
                    slots.DeleteArrayElementAtIndex(slotIndex);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }
                EditorGUILayout.EndHorizontal();

                if (slot.isExpanded)
                {
                    EditorGUI.indentLevel++;
                    DrawPlayerSlot(slot);
                    EditorGUI.indentLevel--;
                }
                EditorGUILayout.EndVertical();
            }

            if (GUILayout.Button(AddPlayerLabel, GUILayout.Height(28f)))
            {
                AddNewPlayer(slots);
            }
        }

        private static void DrawPlayerSlot(SerializedProperty slot)
        {
            DrawPropertyIfPresent(slot, "PlayerId");

            SerializedProperty hasJoinAction = slot.FindPropertyRelative("HasJoinAction");
            SerializedProperty joinAction = slot.FindPropertyRelative("JoinAction");
            if (hasJoinAction != null)
            {
                EditorGUILayout.PropertyField(hasJoinAction, new GUIContent("Has Join Action"));
                if (hasJoinAction.boolValue && joinAction != null)
                {
                    EditorGUI.indentLevel++;
                    EditorGUILayout.PropertyField(joinAction, true);
                    EditorGUI.indentLevel--;
                }
            }
            else if (joinAction != null)
            {
                EditorGUILayout.PropertyField(joinAction, true);
            }

            DrawPropertyIfPresent(slot, "DefaultControlScheme");
            DrawPropertyIfPresent(slot, "ControlSchemes", true);
            DrawPropertyIfPresent(slot, "Contexts", true);
        }

        private static void DrawPropertyIfPresent(
            SerializedProperty parent,
            string relativeName,
            bool includeChildren = false)
        {
            SerializedProperty property = parent.FindPropertyRelative(relativeName);
            if (property != null)
            {
                EditorGUILayout.PropertyField(property, includeChildren);
            }
        }

        private bool LoadUserConfig()
        {
            if (!UpdateUserConfigPath())
            {
                return false;
            }
            if (!File.Exists(_userConfigPath))
            {
                SetStatus(
                    "The user configuration does not exist. Load or generate a default configuration first.",
                    MessageType.Info);
                return false;
            }

            return LoadConfigFromPath(_userConfigPath, "Loaded the user configuration.");
        }

        private bool LoadDefaultConfig()
        {
            if (!UpdateDefaultConfigPath())
            {
                return false;
            }
            if (!File.Exists(_defaultConfigPath))
            {
                SetStatus($"Default config not found at: {_defaultConfigAssetPath}", MessageType.Warning);
                return false;
            }

            return LoadConfigFromPath(_defaultConfigPath, "Loaded the default configuration.");
        }

        private bool LoadConfigFromPath(string path, string successStatus)
        {
            try
            {
                string yaml = SystemFileStore.Default.ReadText(
                    path,
                    MaxConfigBytes,
                    StrictUtf8,
                    detectByteOrderMark: false);
                if (!InputConfigurationYamlPreflight.TryValidate(yaml, out string yamlError))
                {
                    SetStatus($"Cannot load configuration: {yamlError}", MessageType.Error);
                    return false;
                }
                InputConfiguration model = YamlSerializer.Deserialize<InputConfiguration>(
                    Encoding.UTF8.GetBytes(yaml));
                InputEditorValidationResult validation = InputEditorConfigurationValidator.Validate(model);
                if (!validation.IsValid)
                {
                    SetStatus($"Cannot load configuration: {validation.Error}", MessageType.Error);
                    return false;
                }

                var nextWorkingCopy = CreateInstance<InputConfigurationSO>();
                nextWorkingCopy.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
                try
                {
                    nextWorkingCopy.FromData(validation.PreparedConfiguration ?? model);
                }
                catch (Exception)
                {
                    DestroyImmediate(nextWorkingCopy);
                    throw;
                }

                DestroyWorkingCopy();
                _configSO = nextWorkingCopy;
                _serializedConfig = new SerializedObject(_configSO);
                _validationCacheDirty = true;
                RebuildValidationCache();
                _validationCacheDirty = false;

                if (!string.IsNullOrEmpty(model.SchemaFingerprint) &&
                    !string.Equals(
                        model.SchemaFingerprint,
                        InputSchemaFingerprint.EditorDiagnosticCurrent,
                        StringComparison.Ordinal))
                {
                    SetStatus(
                        "The optional Editor schema diagnostic differs from the current model. Validation passed; saving will refresh it.",
                        MessageType.Warning);
                }
                else
                {
                    SetStatus(successStatus, MessageType.Info);
                }

                return true;
            }
            catch (Exception exception) when (
                exception is not OutOfMemoryException &&
                exception is not AccessViolationException &&
                exception is not StackOverflowException)
            {
                SetStatus(
                    $"Failed to load or parse config ({exception.GetType().Name}).",
                    MessageType.Error);
                return false;
            }
        }

        private void SaveChangesToUserConfig(bool generateConstants = false)
        {
            if (!UpdateUserConfigPath() || !TryPrepareConfigurationForSave(out InputConfiguration model))
            {
                return;
            }

            if (!TryWriteConfiguration(_userConfigPath, model, out string backupPath))
            {
                return;
            }

            SetStatus(
                BuildWriteSuccessMessage("Saved user configuration", UserConfigFileName, backupPath),
                GetWriteSuccessMessageType(backupPath));
            if (generateConstants)
            {
                GenerateConstantsFile(model);
            }
        }

        private void ResetToDefault()
        {
            if (!LoadDefaultConfig())
            {
                SetStatus("Cannot reset because the default configuration could not be loaded.", MessageType.Error);
                return;
            }

            SaveChangesToUserConfig();
        }

        private void GenerateDefaultConfigFile()
        {
            if (!UpdateDefaultConfigPath())
            {
                return;
            }
            if (File.Exists(_defaultConfigPath) &&
                !EditorUtility.DisplayDialog(
                    "Overwrite Default Config?",
                    "A backup will be created before the default configuration is replaced.",
                    "Overwrite",
                    "Cancel"))
            {
                return;
            }

            InputConfiguration model = CreateDefaultConfigTemplate();
            InputEditorValidationResult validation = InputEditorConfigurationValidator.Validate(model);
            if (!validation.IsValid)
            {
                SetStatus($"The default template is invalid: {validation.Error}", MessageType.Error);
                return;
            }
            if (!TryPreflightConfiguration(model, "The default template")) return;

            if (!TryWriteConfiguration(_defaultConfigPath, model, out string backupPath))
            {
                return;
            }

            InputEditorFileUtility.ImportAssetAtPath(_defaultConfigAssetPath);
            SetStatus(
                BuildWriteSuccessMessage("Generated default configuration", _defaultConfigAssetPath, backupPath),
                GetWriteSuccessMessageType(backupPath));
            LoadDefaultConfig();
        }

        private void OverrideDefaultConfig()
        {
            if (!UpdateDefaultConfigPath() || !TryPrepareConfigurationForSave(out InputConfiguration model))
            {
                return;
            }
            if (!EditorUtility.DisplayDialog(
                    "Save Project Default?",
                    $"Save the working configuration to {_defaultConfigAssetPath}? A backup will be created when the file exists.",
                    "Save",
                    "Cancel"))
            {
                return;
            }

            if (!TryWriteConfiguration(_defaultConfigPath, model, out string backupPath))
            {
                return;
            }

            InputEditorFileUtility.ImportAssetAtPath(_defaultConfigAssetPath);
            SetStatus(
                BuildWriteSuccessMessage("Overrode default configuration", _defaultConfigAssetPath, backupPath),
                GetWriteSuccessMessageType(backupPath));
        }

        private bool TryPrepareConfigurationForSave(out InputConfiguration model)
        {
            model = null;
            if (_configSO == null || _serializedConfig == null)
            {
                SetStatus("No configuration is loaded.", MessageType.Error);
                return false;
            }

            _serializedConfig.ApplyModifiedProperties();
            model = _configSO.ToData();
            model.SchemaVersion = InputConfiguration.CurrentSchemaVersion;
            model.SchemaFingerprint = InputSchemaFingerprint.EditorDiagnosticCurrent;

            InputEditorValidationResult validation = InputEditorConfigurationValidator.Validate(model);
            if (!validation.IsValid)
            {
                SetStatus($"Cannot save configuration: {validation.Error}", MessageType.Error);
                return false;
            }

            if (!TryPreflightConfiguration(model, "Cannot save configuration")) return false;

            return true;
        }

        private bool TryPreflightConfiguration(InputConfiguration model, string label)
        {
            InputConfigurationPreflightResult preflight =
                InputSystemConfigurationPreflight.Validate(model);
            if (preflight.IsSuccess) return true;
            string detail = preflight.Issues.Count == 0
                ? preflight.Status.ToString()
                : preflight.Issues[0].ToString();
            SetStatus($"{label}: Input System preflight failed. {detail}", MessageType.Error);
            return false;
        }

        private bool TryWriteConfiguration(
            string path,
            InputConfiguration model,
            out string backupPath)
        {
            backupPath = null;
            try
            {
                byte[] bytes = SerializeConfiguration(model);
                if (bytes.Length > MaxConfigBytes)
                {
                    SetStatus(
                        $"Serialized configuration exceeds the {MaxConfigBytes}-byte runtime limit.",
                        MessageType.Error);
                    return false;
                }

                if (!InputEditorFileUtility.TryWriteBytesTransactional(
                        path,
                        bytes,
                        out backupPath,
                        out string error))
                {
                    SetStatus(error, MessageType.Error);
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                SetStatus(
                    $"Failed to serialize configuration ({exception.GetType().Name}).",
                    MessageType.Error);
                return false;
            }
        }

        private static byte[] SerializeConfiguration(InputConfiguration model)
        {
            var writer = new ArrayBufferWriter<byte>();
            var emitter = new Utf8YamlEmitter(writer);
            YamlSerializer.Serialize(ref emitter, model);
            return writer.WrittenSpan.ToArray();
        }

        private static string BuildWriteSuccessMessage(string operation, string path, string backupPath)
        {
            if (IsRecoveryBackupPath(backupPath))
            {
                return $"{operation}: {path}. The prior file remains in recovery backup " +
                       $"{Path.GetFileName(backupPath)} because fixed-backup promotion did not complete.";
            }

            return string.IsNullOrEmpty(backupPath)
                ? $"{operation}: {path}"
                : $"{operation}: {path}. Backup: {Path.GetFileName(backupPath)}";
        }

        private static MessageType GetWriteSuccessMessageType(string backupPath)
        {
            return IsRecoveryBackupPath(backupPath) ? MessageType.Warning : MessageType.Info;
        }

        private static bool IsRecoveryBackupPath(string backupPath)
        {
            return !string.IsNullOrEmpty(backupPath) &&
                   backupPath.IndexOf(".bak.tmp.", StringComparison.Ordinal) >= 0;
        }

        private void ClearEditor()
        {
            DestroyWorkingCopy();
            _validationCacheDirty = true;
            _validationMessage = null;
            _validationMessageType = MessageType.None;
        }

        private void DestroyWorkingCopy()
        {
            _serializedConfig = null;
            if (_configSO != null)
            {
                DestroyImmediate(_configSO);
                _configSO = null;
            }
        }

        private void SetStatus(string message, MessageType type)
        {
            _statusMessage = InputEditorFileUtility.ToSafeDisplayText(message);
            _statusMessageType = type;
            Repaint();
        }
    }
}
