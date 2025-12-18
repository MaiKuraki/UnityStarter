using UnityEngine;
using UnityEditor;
using System.IO;
using VYaml.Serialization;
using VYaml.Emitter;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Buffers;
using CycloneGames.Utility.Runtime;
using CycloneGames.InputSystem.Runtime;

namespace CycloneGames.InputSystem.Editor
{
    public class InputEditorWindow : EditorWindow
    {
        private InputConfigurationSO _configSO;
        private SerializedObject _serializedConfig;
        private Vector2 _scrollPosition;
        private GUIStyle _overflowLabelStyle;
        private GUIStyle _toolbarSectionLabelStyle;
        private bool _isProSkin;
        private Dictionary<string, Color> _sectionColors;
        private Dictionary<string, GUIStyle> _sectionButtonStyles;
        private Dictionary<string, Texture2D> _sectionButtonTextures;
        private string _defaultConfigPath;
        private string _userConfigPath;
        private string _statusMessage;
        private MessageType _statusMessageType = MessageType.Info;
        private string _codegenPath;
        private string _codegenNamespace;
        private DefaultAsset _codegenFolder;
        private string _defaultConfigFolderPath;
        private DefaultAsset _defaultConfigFolder;
        private string _previousDefaultConfigFolderPath;
        private string _userConfigSubPath;
        private string _userConfigFullPathDisplay;
        private string _defaultConfigFullPathDisplay;
        private string _lastValidationHash;
        
        private HashSet<string> _cachedContextNames = new HashSet<string>();
        private HashSet<string> _cachedActionMapNames = new HashSet<string>();
        private Dictionary<string, string> _contextNameToLocation = new Dictionary<string, string>();
        private Dictionary<string, string> _actionMapNameToLocation = new Dictionary<string, string>();
        private bool _validationCacheDirty = true;
        private Dictionary<int, int> _previousContextCounts = new Dictionary<int, int>();

        private const string DefaultConfigFileName = "input_config.yaml";
        private const string UserConfigFileName = "user_input_settings.yaml";

        private List<(string label, System.Action drawButtons, float estimatedWidth)> _toolbarSections;

        [MenuItem("Tools/CycloneGames/Input System Editor")]
        public static void ShowWindow()
        {
            GetWindow<InputEditorWindow>("Input System Editor");
        }

        private void OnEnable()
        {
            _userConfigSubPath = EditorPrefs.GetString("CycloneGames.InputSystem.UserConfigSubPath", "");
            UpdateUserConfigPath();

            _codegenPath = EditorPrefs.GetString("CycloneGames.InputSystem.CodegenPath", "Assets");
            _codegenNamespace = EditorPrefs.GetString("CycloneGames.InputSystem.CodegenNamespace", "YourGame.Input.Generated");
            if (!string.IsNullOrEmpty(_codegenPath))
            {
                _codegenFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(_codegenPath);
            }

            _defaultConfigFolderPath = EditorPrefs.GetString("CycloneGames.InputSystem.DefaultConfigFolder", "Assets/StreamingAssets");
            _previousDefaultConfigFolderPath = EditorPrefs.GetString("CycloneGames.InputSystem.PreviousDefaultConfigFolder", "");
            if (!string.IsNullOrEmpty(_defaultConfigFolderPath))
            {
                _defaultConfigFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(_defaultConfigFolderPath);
            }
            if (_defaultConfigFolder == null)
            {
                _defaultConfigFolderPath = "Assets/StreamingAssets";
                _defaultConfigFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(_defaultConfigFolderPath);
            }

            UpdateDefaultConfigPath();

            InitializeToolbarSections();

            LoadUserConfig();
            
            if (_configSO != null && _serializedConfig != null)
            {
                _validationCacheDirty = true;
                var slotsProp = _serializedConfig.FindProperty("_playerSlots");
                if (slotsProp != null && slotsProp.isArray)
                {
                    for (int i = 0; i < slotsProp.arraySize; i++)
                    {
                        var slotProp = slotsProp.GetArrayElementAtIndex(i);
                        var contextsProp = slotProp.FindPropertyRelative("Contexts");
                        if (contextsProp != null)
                        {
                            _previousContextCounts[i] = contextsProp.arraySize;
                        }
                    }
                }
            }
        }

        private void UpdateUserConfigPath()
        {
            // Build the full path: PersistentData + SubPath + FileName
            string fullPath = UserConfigFileName;
            if (!string.IsNullOrEmpty(_userConfigSubPath))
            {
                string normalizedSubPath = _userConfigSubPath.Trim('/', '\\');
                if (!string.IsNullOrEmpty(normalizedSubPath))
                {
                    fullPath = normalizedSubPath + "/" + UserConfigFileName;
                }
            }

            _userConfigPath = FilePathUtility.GetUnityWebRequestUri(fullPath, UnityPathSource.PersistentData);

            string localPath = new System.Uri(_userConfigPath).LocalPath;
            _userConfigFullPathDisplay = localPath.Replace('\\', '/');
        }

        private void UpdateDefaultConfigPath()
        {
            if (string.IsNullOrEmpty(_defaultConfigFolderPath) || _defaultConfigFolder == null)
            {
                _defaultConfigPath = FilePathUtility.GetUnityWebRequestUri(DefaultConfigFileName, UnityPathSource.StreamingAssets);
                string localPath = new System.Uri(_defaultConfigPath).LocalPath;
                _defaultConfigFullPathDisplay = localPath.Replace('\\', '/');
                return;
            }

            string folderPath = _defaultConfigFolderPath.TrimEnd('/', '\\');
            if (folderPath.StartsWith("Assets/", System.StringComparison.OrdinalIgnoreCase) ||
                folderPath.StartsWith("Assets\\", System.StringComparison.OrdinalIgnoreCase))
            {
                folderPath = folderPath.Substring(7);
            }
            else if (folderPath.Equals("Assets", System.StringComparison.OrdinalIgnoreCase))
            {
                folderPath = "";
            }

            string fullSystemPath = Path.Combine(Application.dataPath, folderPath, DefaultConfigFileName);
            _defaultConfigPath = FilePathUtility.GetUnityWebRequestUri(fullSystemPath, UnityPathSource.AbsoluteOrFullUri);

            _defaultConfigFullPathDisplay = fullSystemPath.Replace('\\', '/');
        }

        private void HandleConfigFolderChange(string oldFolderPath, string newFolderPath)
        {
            if (string.IsNullOrEmpty(oldFolderPath) || oldFolderPath.Equals(newFolderPath, System.StringComparison.OrdinalIgnoreCase))
            {
                EditorPrefs.SetString("CycloneGames.InputSystem.PreviousDefaultConfigFolder", newFolderPath);
                return;
            }

            string oldAssetPath = GetConfigAssetPath(oldFolderPath);
            string newAssetPath = GetConfigAssetPath(newFolderPath);

            if (!AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(oldAssetPath))
            {
                EditorPrefs.SetString("CycloneGames.InputSystem.PreviousDefaultConfigFolder", newFolderPath);
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(newAssetPath))
            {
                int choice = EditorUtility.DisplayDialogComplex(
                    "Default Config File Exists",
                    $"A default config file already exists in the new location:\n{newAssetPath}\n\n" +
                    $"The old file is at:\n{oldAssetPath}\n\n" +
                    "How would you like to proceed?",
                    "Move (Replace)", "Delete Old", "Cancel");

                if (choice == 0)
                {
                    if (!AssetDatabase.DeleteAsset(newAssetPath))
                    {
                        EditorUtility.DisplayDialog("Error", "Failed to delete existing file.", "OK");
                        return;
                    }
                    AssetDatabase.Refresh();

                    string moveError = AssetDatabase.MoveAsset(oldAssetPath, newAssetPath);
                    if (!string.IsNullOrEmpty(moveError))
                    {
                        EditorUtility.DisplayDialog("Error", $"Failed to move file: {moveError}", "OK");
                        return;
                    }
                    AssetDatabase.Refresh();
                    SetStatus($"Moved default config from {oldFolderPath} to {newFolderPath}", MessageType.Info);
                }
                else if (choice == 1)
                {
                    if (!AssetDatabase.DeleteAsset(oldAssetPath))
                    {
                        EditorUtility.DisplayDialog("Error", "Failed to delete old file.", "OK");
                        return;
                    }
                    AssetDatabase.Refresh();
                    SetStatus($"Deleted old default config from {oldFolderPath}", MessageType.Info);
                }
                else
                {
                    _defaultConfigFolderPath = oldFolderPath;
                    _defaultConfigFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(oldFolderPath);
                    UpdateDefaultConfigPath();
                    return;
                }
            }
            else
            {
                int choice = EditorUtility.DisplayDialogComplex(
                    "Move Default Config File?",
                    $"The default config file will be moved from:\n{oldAssetPath}\n\n" +
                    $"To:\n{newAssetPath}\n\n" +
                    "How would you like to proceed?",
                    "Move", "Delete Old", "Cancel");

                if (choice == 0)
                {
                    string moveError = AssetDatabase.MoveAsset(oldAssetPath, newAssetPath);
                    if (!string.IsNullOrEmpty(moveError))
                    {
                        EditorUtility.DisplayDialog("Error", $"Failed to move file: {moveError}", "OK");
                        return;
                    }
                    AssetDatabase.Refresh();
                    SetStatus($"Moved default config from {oldFolderPath} to {newFolderPath}", MessageType.Info);
                }
                else if (choice == 1)
                {
                    if (!AssetDatabase.DeleteAsset(oldAssetPath))
                    {
                        EditorUtility.DisplayDialog("Error", "Failed to delete old file.", "OK");
                        return;
                    }
                    AssetDatabase.Refresh();
                    SetStatus($"Deleted old default config from {oldFolderPath}", MessageType.Info);
                }
                else
                {
                    _defaultConfigFolderPath = oldFolderPath;
                    _defaultConfigFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(oldFolderPath);
                    UpdateDefaultConfigPath();
                    return;
                }
            }

            EditorPrefs.SetString("CycloneGames.InputSystem.PreviousDefaultConfigFolder", newFolderPath);
        }

        private string GetConfigAssetPath(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath))
            {
                return Path.Combine("Assets", "StreamingAssets", DefaultConfigFileName).Replace('\\', '/');
            }

            string cleanPath = folderPath.TrimEnd('/', '\\');
            if (!cleanPath.StartsWith("Assets/", System.StringComparison.OrdinalIgnoreCase) &&
                !cleanPath.StartsWith("Assets\\", System.StringComparison.OrdinalIgnoreCase) &&
                !cleanPath.Equals("Assets", System.StringComparison.OrdinalIgnoreCase))
            {
                cleanPath = "Assets/" + cleanPath;
            }

            return Path.Combine(cleanPath, DefaultConfigFileName).Replace('\\', '/');
        }

        private byte[] SerializeConfigWithoutNullJoinAction(InputConfiguration config)
        {
            var bufferWriter = new ArrayBufferWriter<byte>();
            var emitter = new Utf8YamlEmitter(bufferWriter);

            if (config.JoinAction == null)
            {
                var configWithoutJoinAction = new InputConfiguration
                {
                    PlayerSlots = config.PlayerSlots
                };
                YamlSerializer.Serialize(ref emitter, configWithoutJoinAction);
            }
            else
            {
                YamlSerializer.Serialize(ref emitter, config);
            }

            return bufferWriter.WrittenSpan.ToArray();
        }

        private void OnDisable()
        {
            if (_sectionButtonTextures != null)
            {
                foreach (var texture in _sectionButtonTextures.Values)
                {
                    if (texture != null) DestroyImmediate(texture);
                }
                _sectionButtonTextures.Clear();
            }
        }

        private void InitializeToolbarSections()
        {
            _toolbarSections = new List<(string label, System.Action drawButtons, float estimatedWidth)>
            {
                ("Load", () =>
                {
                    if (GUILayout.Button("User Config", _sectionButtonStyles["Load"])) LoadUserConfig();
                    if (GUILayout.Button("Default Config", _sectionButtonStyles["Load"])) LoadDefaultConfig();
                }, 250f),
                ("Generate", () =>
                {
                    if (GUILayout.Button("Default Config", _sectionButtonStyles["Generate"])) GenerateDefaultConfigFile();
                    GUI.enabled = _configSO != null;
                    if (GUILayout.Button("Override Default", _sectionButtonStyles["Generate"])) OverrideDefaultConfig();
                    GUI.enabled = true;
                }, 280f),
                ("Save", () =>
                {
                    GUI.enabled = _configSO != null;
                    if (GUILayout.Button("User Config", _sectionButtonStyles["Save"])) SaveChangesToUserConfig();
                    if (GUILayout.Button("User + Generate", _sectionButtonStyles["Save"]))
                    {
                        SaveChangesToUserConfig(true);
                    }
                    GUI.enabled = true;
                }, 270f),
                ("Reset", () =>
                {
                    if (GUILayout.Button("User to Default", _sectionButtonStyles["Reset"]))
                    {
                        if (EditorUtility.DisplayDialog("Reset User Configuration?", "This will overwrite your user settings with the default configuration. This cannot be undone.", "Reset", "Cancel"))
                        {
                            ResetToDefault();
                        }
                    }
                }, 200f)
            };
        }

        private void OnGUI()
        {
            bool isProSkin = EditorGUIUtility.isProSkin;
            bool themeChanged = _isProSkin != isProSkin;

            if (_sectionColors == null || _sectionColors.Count == 0 || themeChanged)
            {
                InitializeSectionColors(isProSkin);
            }

            if (_overflowLabelStyle == null)
            {
                _overflowLabelStyle = new GUIStyle(EditorStyles.label)
                {
                    clipping = TextClipping.Overflow,
                    wordWrap = false,
                    alignment = TextAnchor.MiddleLeft
                };
            }
            if (_toolbarSectionLabelStyle == null)
            {
                _toolbarSectionLabelStyle = new GUIStyle(EditorStyles.label)
                {
                    fontStyle = FontStyle.Bold,
                    alignment = TextAnchor.MiddleCenter,
                    padding = new RectOffset(0, 0, 0, 0),
                    margin = new RectOffset(0, 0, 0, 0),
                    border = new RectOffset(0, 0, 0, 0)
                };
                _toolbarSectionLabelStyle.normal.textColor = isProSkin ? new Color(0.9f, 0.9f, 0.95f, 1f) : new Color(0.15f, 0.15f, 0.2f, 1f);
            }

            if (themeChanged)
            {
                _toolbarSectionLabelStyle.normal.textColor = isProSkin ? new Color(0.9f, 0.9f, 0.95f, 1f) : new Color(0.15f, 0.15f, 0.2f, 1f);
            }

            UpdateSectionButtonStyles(isProSkin);

            DrawToolbar();
            DrawStatusBar();
            DrawCodegenSettings();

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            if (_serializedConfig != null && _configSO != null)
            {
                _serializedConfig.Update();

                var slotsProp = _serializedConfig.FindProperty("_playerSlots");
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Player Slots", EditorStyles.boldLabel);
                if (GUILayout.Button("+ Add Player", GUILayout.Width(100)))
                {
                    AddNewPlayer(slotsProp);
                }
                EditorGUILayout.EndHorizontal();

                if (slotsProp.arraySize > 0)
                {
                    for (int i = 0; i < slotsProp.arraySize; i++)
                    {
                        var slotProp = slotsProp.GetArrayElementAtIndex(i);
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField($"Player {i}", EditorStyles.boldLabel);
                        if (GUILayout.Button("Remove", GUILayout.Width(60)))
                        {
                            slotsProp.DeleteArrayElementAtIndex(i);
                            break;
                        }
                        EditorGUILayout.EndHorizontal();

                        EditorGUI.indentLevel++;
                        EditorGUILayout.PropertyField(slotProp.FindPropertyRelative("PlayerId"));
                        EditorGUILayout.LabelField("Join Action", EditorStyles.boldLabel);
                        EditorGUI.indentLevel++;
                        var joinTypeProp = slotProp.FindPropertyRelative("JoinAction.Type");
                        var joinActionProp = slotProp.FindPropertyRelative("JoinAction.ActionName");
                        var joinBindingsProp = slotProp.FindPropertyRelative("JoinAction.DeviceBindings");
                        var joinLongPressProp = slotProp.FindPropertyRelative("JoinAction.LongPressMs");

                        EditorGUILayout.PropertyField(joinTypeProp);
                        
                        EditorGUI.BeginChangeCheck();
                        EditorGUILayout.PropertyField(joinActionProp);
                        if (EditorGUI.EndChangeCheck())
                        {
                            _validationCacheDirty = true;
                        }
                        
                        EditorGUILayout.PropertyField(joinBindingsProp, true);

                        var joinType = (CycloneGames.InputSystem.Runtime.ActionValueType)joinTypeProp.enumValueIndex;
                        if (joinType == CycloneGames.InputSystem.Runtime.ActionValueType.Button)
                        {
                            EditorGUILayout.BeginHorizontal();
                            EditorGUILayout.LabelField("Long Press (ms)", _overflowLabelStyle, GUILayout.Width(220));
                            EditorGUILayout.PropertyField(joinLongPressProp, GUIContent.none, true);
                            EditorGUILayout.EndHorizontal();
                        }
                        else if (joinType == CycloneGames.InputSystem.Runtime.ActionValueType.Float)
                        {
                            var joinThresholdProp = slotProp.FindPropertyRelative("JoinAction.LongPressValueThreshold");
                            EditorGUILayout.BeginHorizontal();
                            EditorGUILayout.LabelField("Long Press (ms)", _overflowLabelStyle, GUILayout.Width(220));
                            EditorGUILayout.PropertyField(joinLongPressProp, GUIContent.none, true);
                            EditorGUILayout.EndHorizontal();
                            EditorGUILayout.BeginHorizontal();
                            EditorGUILayout.LabelField("Long Press Threshold (0-1)", _overflowLabelStyle, GUILayout.Width(220));
                            EditorGUILayout.PropertyField(joinThresholdProp, GUIContent.none, true);
                            EditorGUILayout.EndHorizontal();
                        }
                        EditorGUI.indentLevel--;

                        var contextsProp = slotProp.FindPropertyRelative("Contexts");
                        
                        int previousCount = _previousContextCounts.TryGetValue(i, out var count) ? count : contextsProp.arraySize;
                        
                        EditorGUI.BeginChangeCheck();
                        EditorGUILayout.PropertyField(contextsProp, new GUIContent("Contexts"), true);
                        bool contextsChanged = EditorGUI.EndChangeCheck();
                        
                        int currentCount = contextsProp.arraySize;
                        
                        if (currentCount > previousCount)
                        {
                            _serializedConfig.ApplyModifiedProperties();
                            _serializedConfig.Update();
                            
                            RebuildValidationCache();
                            
                            contextsProp = slotProp.FindPropertyRelative("Contexts");
                            
                            bool nameChanged = false;
                            for (int newIdx = previousCount; newIdx < currentCount; newIdx++)
                            {
                                var newCtxProp = contextsProp.GetArrayElementAtIndex(newIdx);
                                var newCtxNameProp = newCtxProp.FindPropertyRelative("Name");
                                var newCtxActionMapProp = newCtxProp.FindPropertyRelative("ActionMap");
                                
                                string currentName = newCtxNameProp != null ? newCtxNameProp.stringValue : "";
                                string currentActionMap = newCtxActionMapProp != null ? newCtxActionMapProp.stringValue : "";
                                
                                if (newCtxNameProp != null)
                                {
                                    if (string.IsNullOrEmpty(currentName) || _cachedContextNames.Contains(currentName))
                                    {
                                        newCtxNameProp.stringValue = GenerateUniqueContextName(i, string.IsNullOrEmpty(currentName) ? null : currentName);
                                        nameChanged = true;
                                    }
                                }
                                
                                if (newCtxActionMapProp != null)
                                {
                                    if (string.IsNullOrEmpty(currentActionMap) || _cachedActionMapNames.Contains(currentActionMap) || currentActionMap == "GlobalActions")
                                    {
                                        newCtxActionMapProp.stringValue = GenerateUniqueActionMapName(i, string.IsNullOrEmpty(currentActionMap) ? null : currentActionMap);
                                        nameChanged = true;
                                    }
                                }
                            }
                            
                            if (nameChanged)
                            {
                                _serializedConfig.ApplyModifiedProperties();
                                _serializedConfig.Update();
                                RebuildValidationCache();
                            }
                            
                            _previousContextCounts[i] = currentCount;
                            _validationCacheDirty = false;
                        }
                        else if (contextsChanged)
                        {
                            _validationCacheDirty = true;
                            _previousContextCounts[i] = currentCount;
                        }
                        else
                        {
                            _previousContextCounts[i] = currentCount;
                        }
                        
                        EditorGUI.indentLevel--;

                        if (i < slotsProp.arraySize - 1)
                        {
                            EditorGUILayout.Space();
                            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
                            EditorGUILayout.Space();
                        }
                    }
                }
                else
                {
                    EditorGUILayout.HelpBox("No players configured. Click 'Add Player' to create the first player.", MessageType.Info);
                }

                _serializedConfig.ApplyModifiedProperties();
                
                if (_validationCacheDirty)
                {
                    RebuildValidationCache();
                    _validationCacheDirty = false;
                }
                
                ValidateCurrentValues();
            }
            else
            {
                EditorGUILayout.HelpBox("No configuration loaded. Generate or load a configuration file using the toolbar.", MessageType.Warning);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar()
        {
            if (_toolbarSections == null)
            {
                InitializeToolbarSections();
            }

            float availableWidth = position.width;

            float currentRowWidth = 0f;
            bool isFirstInRow = true;

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            foreach (var section in _toolbarSections)
            {
                if (!isFirstInRow && currentRowWidth + section.estimatedWidth > availableWidth - 30f)
                {
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
                    currentRowWidth = 0f;
                    isFirstInRow = true;
                }

                DrawToolbarSection(section.label, section.drawButtons, _sectionColors[section.label]);

                currentRowWidth += section.estimatedWidth;
                isFirstInRow = false;
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void InitializeSectionColors(bool isProSkin)
        {
            if (_sectionColors == null)
            {
                _sectionColors = new Dictionary<string, Color>();
            }

            _sectionColors["Load"] = isProSkin ? new Color(0.4f, 0.6f, 0.9f, 1f) : new Color(0.2f, 0.4f, 0.8f, 1f);
            _sectionColors["Generate"] = isProSkin ? new Color(0.4f, 0.8f, 0.5f, 1f) : new Color(0.2f, 0.6f, 0.3f, 1f);
            _sectionColors["Save"] = isProSkin ? new Color(0.9f, 0.7f, 0.3f, 1f) : new Color(0.8f, 0.5f, 0.1f, 1f);
            _sectionColors["Reset"] = isProSkin ? new Color(0.9f, 0.4f, 0.4f, 1f) : new Color(0.8f, 0.2f, 0.2f, 1f);
        }

        private void UpdateSectionButtonStyles(bool isProSkin)
        {
            if (_sectionButtonStyles == null)
            {
                _sectionButtonStyles = new Dictionary<string, GUIStyle>();
            }
            if (_sectionButtonTextures == null)
            {
                _sectionButtonTextures = new Dictionary<string, Texture2D>();
            }

            bool needRecreateTextures = (_isProSkin != isProSkin || _sectionButtonTextures.Count == 0);

            if (needRecreateTextures)
            {
                foreach (var texture in _sectionButtonTextures.Values)
                {
                    if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
                }
                _sectionButtonTextures.Clear();
                _isProSkin = isProSkin;
            }
            else
            {
                if (_sectionButtonStyles.Count == _sectionColors.Count) return;
            }

            foreach (var kvp in _sectionColors)
            {
                string sectionName = kvp.Key;
                Color sectionColor = kvp.Value;

                if (!_sectionButtonStyles.ContainsKey(sectionName))
                {
                    _sectionButtonStyles[sectionName] = new GUIStyle(EditorStyles.toolbarButton);
                }

                GUIStyle style = _sectionButtonStyles[sectionName];

                // Update text colors to match section color
                style.normal.textColor = sectionColor;
                style.hover.textColor = Color.Lerp(sectionColor, Color.white, 0.3f);
                style.active.textColor = Color.Lerp(sectionColor, Color.black, 0.2f);
                style.focused.textColor = sectionColor;
                style.onNormal.textColor = sectionColor;
                style.onHover.textColor = Color.Lerp(sectionColor, Color.white, 0.3f);
                style.onActive.textColor = Color.Lerp(sectionColor, Color.black, 0.2f);
                style.onFocused.textColor = sectionColor;

                // Create or reuse background textures with section color
                string normalKey = sectionName + "_normal";
                string hoverKey = sectionName + "_hover";
                string activeKey = sectionName + "_active";

                if (needRecreateTextures || !_sectionButtonTextures.ContainsKey(normalKey))
                {
                    _sectionButtonTextures[normalKey] = CreateColorTexture(sectionColor * (isProSkin ? 0.3f : 0.15f));
                    _sectionButtonTextures[hoverKey] = CreateColorTexture(sectionColor * (isProSkin ? 0.5f : 0.3f));
                    _sectionButtonTextures[activeKey] = CreateColorTexture(sectionColor * (isProSkin ? 0.4f : 0.25f));
                }

                // Set background textures
                style.normal.background = _sectionButtonTextures[normalKey];
                style.hover.background = _sectionButtonTextures[hoverKey];
                style.active.background = _sectionButtonTextures[activeKey];
                style.focused.background = _sectionButtonTextures[normalKey];
                style.onNormal.background = _sectionButtonTextures[normalKey];
                style.onHover.background = _sectionButtonTextures[hoverKey];
                style.onActive.background = _sectionButtonTextures[activeKey];
                style.onFocused.background = _sectionButtonTextures[normalKey];
            }
        }

        private Texture2D CreateColorTexture(Color color)
        {
            Texture2D texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            texture.SetPixel(0, 0, color);
            texture.Apply();
            texture.hideFlags = HideFlags.HideAndDontSave;
            return texture;
        }

        private void DrawToolbarSection(string label, System.Action drawButtons, Color sectionColor)
        {
            float toolbarHeight = EditorStyles.toolbar.fixedHeight > 0 ? EditorStyles.toolbar.fixedHeight : EditorGUIUtility.singleLineHeight;

            Rect labelRect = GUILayoutUtility.GetRect(75, toolbarHeight, GUILayout.Width(75));

            GUI.Label(labelRect, label, _toolbarSectionLabelStyle);

            EditorGUI.DrawRect(new Rect(labelRect.x, labelRect.yMax - 2, labelRect.width, 2), sectionColor);

            GUILayout.Space(8);

            drawButtons();

            GUILayout.Space(12);

            DrawToolbarSeparator();
            GUILayout.Space(4);
        }

        private void DrawToolbarSeparator()
        {
            bool isProSkin = EditorGUIUtility.isProSkin;
            Rect rect = GUILayoutUtility.GetRect(1, EditorGUIUtility.singleLineHeight, GUILayout.Width(1));
            Color separatorColor = isProSkin ? new Color(0.3f, 0.3f, 0.3f, 0.8f) : new Color(0.6f, 0.6f, 0.6f, 0.8f);
            EditorGUI.DrawRect(rect, separatorColor);
        }

        private void DrawStatusBar()
        {
            if (!string.IsNullOrEmpty(_statusMessage))
            {
                EditorGUILayout.HelpBox(_statusMessage, _statusMessageType);
            }
        }

        private void DrawCodegenSettings()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Configuration Settings", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            EditorGUI.BeginChangeCheck();
            var newDefaultConfigFolder = (DefaultAsset)EditorGUILayout.ObjectField("Default Config Folder", _defaultConfigFolder, typeof(DefaultAsset), false);
            if (EditorGUI.EndChangeCheck())
            {
                if (newDefaultConfigFolder != _defaultConfigFolder)
                {
                    string oldFolderPath = _defaultConfigFolderPath;
                    _defaultConfigFolder = newDefaultConfigFolder;
                    if (_defaultConfigFolder != null)
                    {
                        _defaultConfigFolderPath = AssetDatabase.GetAssetPath(_defaultConfigFolder);
                        HandleConfigFolderChange(oldFolderPath, _defaultConfigFolderPath);
                        EditorPrefs.SetString("CycloneGames.InputSystem.DefaultConfigFolder", _defaultConfigFolderPath);
                        UpdateDefaultConfigPath();
                    }
                    else
                    {
                        _defaultConfigFolderPath = "Assets/StreamingAssets";
                        HandleConfigFolderChange(oldFolderPath, _defaultConfigFolderPath);
                        EditorPrefs.SetString("CycloneGames.InputSystem.DefaultConfigFolder", _defaultConfigFolderPath);
                        _defaultConfigFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(_defaultConfigFolderPath);
                        UpdateDefaultConfigPath();
                    }
                }
            }

            // Display full path for Default Config (or prompt if not loaded)
            EditorGUI.BeginDisabledGroup(true);
            string defaultConfigDisplayText;
            string localPath = new System.Uri(_defaultConfigPath).LocalPath;
            bool fileExists = File.Exists(localPath);
            bool configLoaded = _configSO != null;

            if (fileExists || configLoaded)
            {
                // File exists or config is loaded, show full path
                defaultConfigDisplayText = _defaultConfigFullPathDisplay;
            }
            else
            {
                // File doesn't exist and config not loaded, show prompt
                defaultConfigDisplayText = "Please Load or Generate Default Config first";
            }

            EditorGUILayout.TextField(
                new GUIContent("Full Path", "Complete path where default config is located (updates in real-time). If file doesn't exist, please Load or Generate first."),
                defaultConfigDisplayText
            );
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("User Config Settings", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            // User Config Subdirectory Path Input
            EditorGUI.BeginChangeCheck();
            string newUserConfigSubPath = EditorGUILayout.TextField(
                new GUIContent("Subdirectory Path", "Subdirectory path relative to PersistentData (e.g., \"/Config\" or \"Config\"). Leave empty to save directly in PersistentData."),
                _userConfigSubPath
            );
            if (EditorGUI.EndChangeCheck())
            {
                _userConfigSubPath = newUserConfigSubPath;
                UpdateUserConfigPath();
                EditorPrefs.SetString("CycloneGames.InputSystem.UserConfigSubPath", _userConfigSubPath);
            }

            // Display full path (real-time update when subdirectory changes)
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextField(
                new GUIContent("Full Path", "Complete path where user config will be saved (updates in real-time)"),
                _userConfigFullPathDisplay
            );
            EditorGUI.EndDisabledGroup();

            // Show PersistentData base path for reference
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextField(
                new GUIContent("PersistentData Base", "Base PersistentData directory"),
                Application.persistentDataPath
            );
            EditorGUI.EndDisabledGroup();

            EditorGUI.indentLevel--;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Code Generation Settings", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            EditorGUI.BeginChangeCheck();
            var newFolder = (DefaultAsset)EditorGUILayout.ObjectField("Output Directory", _codegenFolder, typeof(DefaultAsset), false);
            var newNamespace = EditorGUILayout.TextField("Namespace", _codegenNamespace);

            if (EditorGUI.EndChangeCheck())
            {
                if (newFolder != _codegenFolder)
                {
                    _codegenFolder = newFolder;
                    _codegenPath = AssetDatabase.GetAssetPath(_codegenFolder);
                    EditorPrefs.SetString("CycloneGames.InputSystem.CodegenPath", _codegenPath);
                }
                if (newNamespace != _codegenNamespace)
                {
                    _codegenNamespace = newNamespace;
                    EditorPrefs.SetString("CycloneGames.InputSystem.CodegenNamespace", _codegenNamespace);
                }
            }

            EditorGUI.indentLevel--;
            EditorGUI.indentLevel--;
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        }

        private void LoadUserConfig()
        {
            string localPath = new System.Uri(_userConfigPath).LocalPath;
            if (File.Exists(localPath))
            {
                LoadConfigFromPath(localPath, $"Loaded user config from: {localPath}");
            }
            else
            {
                SetStatus($"User config not found. Load default and save to create one, or generate a new default.", MessageType.Info);
                ClearEditor();
            }
        }

        private void LoadDefaultConfig()
        {
            string localPath = new System.Uri(_defaultConfigPath).LocalPath;
            if (File.Exists(localPath))
            {
                LoadConfigFromPath(localPath, $"Loaded default config from: {localPath} (Read-Only)");
            }
            else
            {
                SetStatus($"Default config '{DefaultConfigFileName}' not found in StreamingAssets! You can generate one.", MessageType.Warning);
                ClearEditor();
            }
        }

        private void LoadConfigFromPath(string path, string status)
        {
            try
            {
                string yamlContent = File.ReadAllText(path);
                var configModel = YamlSerializer.Deserialize<InputConfiguration>(System.Text.Encoding.UTF8.GetBytes(yamlContent));

                _configSO = CreateInstance<InputConfigurationSO>();
                _configSO.FromData(configModel);

                _serializedConfig = new SerializedObject(_configSO);
                _validationCacheDirty = true;
                _previousContextCounts.Clear();
                
                var slotsProp = _serializedConfig.FindProperty("_playerSlots");
                if (slotsProp != null && slotsProp.isArray)
                {
                    for (int i = 0; i < slotsProp.arraySize; i++)
                    {
                        var slotProp = slotsProp.GetArrayElementAtIndex(i);
                        var contextsProp = slotProp.FindPropertyRelative("Contexts");
                        if (contextsProp != null)
                        {
                            _previousContextCounts[i] = contextsProp.arraySize;
                        }
                    }
                }
                
                RebuildValidationCache();
                _validationCacheDirty = false;
                SetStatus(status, MessageType.Info);
            }
            catch (System.Exception e)
            {
                SetStatus($"Failed to load or parse config: {e.Message}", MessageType.Error);
                ClearEditor();
            }
        }

        private void SaveChangesToUserConfig(bool generateConstants = false)
        {
            if (_configSO == null)
            {
                SetStatus("No configuration loaded to save.", MessageType.Error);
                return;
            }

            try
            {
                InputConfiguration configModel = _configSO.ToData();
                byte[] yamlBytes = SerializeConfigWithoutNullJoinAction(configModel);
                string yamlContent = System.Text.Encoding.UTF8.GetString(yamlBytes);

                string localPath = new System.Uri(_userConfigPath).LocalPath;
                string directory = Path.GetDirectoryName(localPath);
                if (directory != null && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(localPath, yamlContent);
                SetStatus($"Successfully saved user configuration to: {localPath}", MessageType.Info);

                if (generateConstants)
                {
                    GenerateConstantsFile(configModel);
                }
                else
                {
                    EditorUtility.DisplayDialog("Save Successful", "User input configuration has been saved.", "OK");
                }
            }
            catch (System.Exception e)
            {
                SetStatus($"Failed to save config: {e.Message}", MessageType.Error);
            }
        }

        private void ResetToDefault()
        {
            LoadDefaultConfig();
            if (_configSO != null)
            {
                SaveChangesToUserConfig();
            }
            else
            {
                SetStatus("Cannot reset because the default config file does not exist. Please generate one first.", MessageType.Error);
            }
        }

        private void GenerateDefaultConfigFile()
        {
            string localPath = new System.Uri(_defaultConfigPath).LocalPath;

            if (File.Exists(localPath))
            {
                if (!EditorUtility.DisplayDialog("Overwrite Default Config?", "A default configuration file already exists. Overwriting it will discard its current content.", "Overwrite", "Cancel"))
                {
                    return;
                }
            }

            InputConfiguration defaultConfig = CreateDefaultConfigTemplate();

            try
            {
                byte[] yamlBytes = SerializeConfigWithoutNullJoinAction(defaultConfig);
                string yamlContent = System.Text.Encoding.UTF8.GetString(yamlBytes);

                string directory = Path.GetDirectoryName(localPath);
                if (directory != null && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(localPath, yamlContent);
                SetStatus($"Generated new default config at: {localPath}", MessageType.Info);
                AssetDatabase.Refresh();

                LoadDefaultConfig();
            }
            catch (System.Exception e)
            {
                SetStatus($"Error generating default config: {e.Message}", MessageType.Error);
            }
        }

        private void OverrideDefaultConfig()
        {
            if (_configSO == null)
            {
                SetStatus("No configuration loaded to override with.", MessageType.Error);
                return;
            }

            string localPath = new System.Uri(_defaultConfigPath).LocalPath;

            string folderName = !string.IsNullOrEmpty(_defaultConfigFolderPath) ? _defaultConfigFolderPath : "StreamingAssets";
            if (!EditorUtility.DisplayDialog("Override Default Config?",
                $"This will overwrite the default configuration file in {folderName} with your current editor state.\n\n" +
                $"Path: {localPath}\n\n" +
                $"This action cannot be undone. Continue?",
                "Override", "Cancel"))
            {
                return;
            }

            try
            {
                InputConfiguration configModel = _configSO.ToData();
                byte[] yamlBytes = SerializeConfigWithoutNullJoinAction(configModel);
                string yamlContent = System.Text.Encoding.UTF8.GetString(yamlBytes);

                string directory = Path.GetDirectoryName(localPath);
                if (directory != null && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(localPath, yamlContent);
                SetStatus($"Successfully overridden default config at: {localPath}", MessageType.Info);
                AssetDatabase.Refresh();

                // Reload to show the updated default config
                LoadDefaultConfig();
            }
            catch (System.Exception e)
            {
                SetStatus($"Failed to override default config: {e.Message}", MessageType.Error);
            }
        }

        private void GenerateConstantsFile(InputConfiguration config)
        {
            if (_validationCacheDirty)
            {
                RebuildValidationCache();
                _validationCacheDirty = false;
            }

            if (_statusMessageType == MessageType.Error)
            {
                SetStatus("❌ Cannot generate code: Please fix duplicate names before generating.", MessageType.Error);
                return;
            }

            var contexts = new HashSet<string>();
            var actionMaps = new HashSet<string>();
            
            const string globalActionMap = "GlobalActions";
            actionMaps.Add(globalActionMap);

            if (config.PlayerSlots != null)
            {
                foreach (var slot in config.PlayerSlots)
                {
                    if (slot.Contexts != null)
                    {
                        foreach (var context in slot.Contexts)
                        {
                            if (!string.IsNullOrEmpty(context.Name))
                            {
                                contexts.Add(context.Name);
                            }
                            if (!string.IsNullOrEmpty(context.ActionMap))
                            {
                                if (context.ActionMap == globalActionMap)
                                {
                                    SetStatus($"❌ Cannot generate code: ActionMap \"{globalActionMap}\" is reserved for Join Actions. Please use a different name.", MessageType.Error);
                                    return;
                                }
                                actionMaps.Add(context.ActionMap);
                            }
                        }
                    }
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine("// -- AUTO-GENERATED FILE --");
            sb.AppendLine("// This file is generated by the CycloneGames.InputSystem Editor window.");
            sb.AppendLine("// Do not modify this file manually.");
            sb.AppendLine();
            sb.AppendLine("using CycloneGames.InputSystem.Runtime;");
            sb.AppendLine();
            sb.AppendLine($"namespace {_codegenNamespace}");
            sb.AppendLine("{");
            sb.AppendLine("    public static class InputActions");
            sb.AppendLine("    {");
            
            var usedContextIdentifiers = new HashSet<string>();
            sb.AppendLine("        public static class Contexts");
            sb.AppendLine("        {");
            foreach (var context in contexts.OrderBy(c => c))
            {
                string sanitized = SanitizeIdentifier(context);
                string uniqueIdentifier = GetUniqueIdentifier(sanitized, usedContextIdentifiers);
                sb.AppendLine($"            /// <summary>Context: \"{context}\"</summary>");
                sb.AppendLine($"            public const string {uniqueIdentifier} = \"{context}\";");
            }
            sb.AppendLine("        }");
            sb.AppendLine();
            
            var usedActionMapIdentifiers = new HashSet<string>();
            sb.AppendLine("        public static class ActionMaps");
            sb.AppendLine("        {");
            foreach (var map in actionMaps.OrderBy(a => a))
            {
                string sanitized = SanitizeIdentifier(map);
                string uniqueIdentifier = GetUniqueIdentifier(sanitized, usedActionMapIdentifiers);
                sb.AppendLine($"            /// <summary>ActionMap: \"{map}\"</summary>");
                sb.AppendLine($"            public const string {uniqueIdentifier} = \"{map}\";");
                // Also generate the ID version if needed, but context construction usually uses string.
                // For completeness, we can add _Id suffix for the int hash.
                string idIdentifier = GetUniqueIdentifier(sanitized + "_Id", usedActionMapIdentifiers);
                sb.AppendLine($"            /// <summary>ActionMap ID for: \"{map}\"</summary>");
                sb.AppendLine($"            public static readonly int {idIdentifier} = InputHashUtility.GetDeterministicHashCode(\"{map}\");");
            }
            sb.AppendLine("        }");
            sb.AppendLine();
            
            sb.AppendLine("        public static class Actions");
            sb.AppendLine("        {");
            var usedIdentifiers = new HashSet<string>();
            
            if (config.PlayerSlots != null)
            {
                var allBindings = config.PlayerSlots
                    .Where(slot => slot.Contexts != null)
                    .SelectMany(slot => slot.Contexts)
                    .Where(ctx => ctx.Bindings != null && !string.IsNullOrEmpty(ctx.Name) && !string.IsNullOrEmpty(ctx.ActionMap))
                    .SelectMany(ctx => ctx.Bindings
                        .Where(b => !string.IsNullOrEmpty(b.ActionName))
                        .Select(b => new { Context = ctx.Name, Action = b.ActionName, Map = ctx.ActionMap }))
                    .Distinct();

                foreach (var binding in allBindings.OrderBy(b => b.Context).ThenBy(b => b.Action))
                {
                    string baseConstantName = string.Concat(binding.Context, "_", binding.Action);
                    string constantName = GetUniqueIdentifier(baseConstantName, usedIdentifiers);
                    sb.AppendLine($"            /// <summary>Action: \"{binding.Map}/{binding.Action}\" (Context: {binding.Context})</summary>");
                    sb.AppendLine($"            public static readonly int {SanitizeIdentifier(constantName)} = InputHashUtility.GetActionId(\"{binding.Context}\", \"{binding.Map}\", \"{binding.Action}\");");
                }
            }
            if (config.PlayerSlots != null)
            {
                foreach (var slot in config.PlayerSlots.Where(s => s.JoinAction != null && !string.IsNullOrEmpty(s.JoinAction.ActionName)))
                {
                    const string joinContext = "PlayerJoin";
                    const string joinMap = "GlobalActions";
                    string baseConstantName = string.Concat(joinContext, "_P", slot.PlayerId.ToString(), "_", slot.JoinAction.ActionName);
                    string constantName = GetUniqueIdentifier(baseConstantName, usedIdentifiers);
                    sb.AppendLine($"            /// <summary>Join Action for Player {slot.PlayerId}: \"{joinMap}/{slot.JoinAction.ActionName}\"</summary>");
                    sb.AppendLine($"            public static readonly int {SanitizeIdentifier(constantName)} = InputHashUtility.GetActionId(\"{joinContext}\", \"{joinMap}\", \"{slot.JoinAction.ActionName}\");");
                }
            }
            sb.AppendLine("        }");

            sb.AppendLine("    }");
            sb.AppendLine("}");

            try
            {
                if (_codegenFolder == null || string.IsNullOrEmpty(_codegenPath))
                {
                    SetStatus("Output directory for code generation is not set.", MessageType.Error);
                    return;
                }

                if (!Directory.Exists(_codegenPath))
                {
                    Directory.CreateDirectory(_codegenPath);
                }

                string filePath = Path.Combine(_codegenPath, "InputActions.cs");
                File.WriteAllText(filePath, sb.ToString());

                SetStatus("Successfully saved and generated constants file.", MessageType.Info);
                EditorUtility.DisplayDialog("Save & Generate Successful", "User input configuration has been saved and InputActions.cs has been generated.", "OK");

                AssetDatabase.Refresh();
            }
            catch (System.Exception e)
            {
                SetStatus($"Failed to generate constants file: {e.Message}", MessageType.Error);
            }
        }

        private string SanitizeIdentifier(string name)
        {
            if (string.IsNullOrEmpty(name)) return "_";

            var sb = new StringBuilder();
            char firstChar = name[0];

            if (char.IsLetter(firstChar) || firstChar == '_')
            {
                sb.Append(firstChar);
            }
            else if (char.IsDigit(firstChar))
            {
                sb.Append('_').Append(firstChar);
            }
            else
            {
                sb.Append('_');
            }

            for (int i = 1; i < name.Length; i++)
            {
                char c = name[i];
                if (char.IsLetterOrDigit(c) || c == '_')
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append('_');
                }
            }

            return sb.ToString();
        }

        private string GetUniqueIdentifier(string baseIdentifier, HashSet<string> usedIdentifiers)
        {
            if (string.IsNullOrEmpty(baseIdentifier))
            {
                baseIdentifier = "_";
            }

            string identifier = baseIdentifier;
            int suffix = 1;

            while (usedIdentifiers.Contains(identifier))
            {
                identifier = string.Concat(baseIdentifier, "_", suffix.ToString());
                suffix++;
            }

            usedIdentifiers.Add(identifier);
            return identifier;
        }

        private void ValidateFieldInRealTime(string value, string fieldType, string location, HashSet<string> usedNames, Dictionary<string, string> nameToLocation, string oldValue = null)
        {
            if (!string.IsNullOrEmpty(oldValue) && oldValue != value)
            {
                usedNames.Remove(oldValue);
                nameToLocation.Remove(oldValue);
            }
            
            if (string.IsNullOrEmpty(value))
            {
                return;
            }
            
            if (fieldType == "ActionMap" && value == "GlobalActions")
            {
                SetStatus($"❌ ActionMap name \"GlobalActions\" is reserved for Join Actions. Please use a different name.", MessageType.Error);
                return;
            }
            
            if (usedNames.Contains(value))
            {
                string existingLocation = nameToLocation.TryGetValue(value, out var loc) ? loc : "unknown";
                SetStatus($"❌ Duplicate {fieldType} name: \"{value}\" at {location} (already used at {existingLocation})", MessageType.Error);
            }
            else
            {
                usedNames.Add(value);
                nameToLocation[value] = location;
                if (_statusMessageType == MessageType.Error && _statusMessage != null && _statusMessage.Contains($"Duplicate {fieldType}"))
                {
                    _validationCacheDirty = true;
                }
            }
        }

        private void ValidateCurrentValues()
        {
            if (_configSO == null || _serializedConfig == null) return;

            var tempContextNames = new HashSet<string>();
            var tempActionMapNames = new HashSet<string>();
            var tempContextLocations = new Dictionary<string, string>();
            var tempActionMapLocations = new Dictionary<string, string>();

            const string globalActionMap = "GlobalActions";
            tempActionMapNames.Add(globalActionMap);
            tempActionMapLocations[globalActionMap] = "Global (Join Actions)";

            var slotsProp = _serializedConfig.FindProperty("_playerSlots");
            if (slotsProp != null && slotsProp.isArray)
            {
                for (int i = 0; i < slotsProp.arraySize; i++)
                {
                    var slotProp = slotsProp.GetArrayElementAtIndex(i);
                    var contextsProp = slotProp.FindPropertyRelative("Contexts");
                    
                    if (contextsProp != null && contextsProp.isArray)
                    {
                        for (int ctxIdx = 0; ctxIdx < contextsProp.arraySize; ctxIdx++)
                        {
                            var ctxProp = contextsProp.GetArrayElementAtIndex(ctxIdx);
                            var ctxNameProp = ctxProp.FindPropertyRelative("Name");
                            var ctxActionMapProp = ctxProp.FindPropertyRelative("ActionMap");
                            
                            if (ctxNameProp != null && !string.IsNullOrEmpty(ctxNameProp.stringValue))
                            {
                                string ctxName = ctxNameProp.stringValue;
                                string location = $"Player {i}, Context {ctxIdx}";
                                
                                if (tempContextNames.Contains(ctxName))
                                {
                                    string existingLoc = tempContextLocations.TryGetValue(ctxName, out var loc) ? loc : "unknown";
                                    SetStatus($"❌ Duplicate Context name: \"{ctxName}\" at {location} (already used at {existingLoc})", MessageType.Error);
                                    return;
                                }
                                else
                                {
                                    tempContextNames.Add(ctxName);
                                    tempContextLocations[ctxName] = location;
                                }
                            }
                            
                            if (ctxActionMapProp != null && !string.IsNullOrEmpty(ctxActionMapProp.stringValue))
                            {
                                string actionMapName = ctxActionMapProp.stringValue;
                                string location = $"Player {i}, Context {ctxIdx}";
                                
                                if (actionMapName == "GlobalActions")
                                {
                                    SetStatus($"❌ ActionMap name \"GlobalActions\" is reserved for Join Actions at {location}. Please use a different name.", MessageType.Error);
                                    return;
                                }
                                else if (tempActionMapNames.Contains(actionMapName))
                                {
                                    string existingLoc = tempActionMapLocations.TryGetValue(actionMapName, out var loc) ? loc : "unknown";
                                    SetStatus($"❌ Duplicate ActionMap name: \"{actionMapName}\" at {location} (already used at {existingLoc})", MessageType.Error);
                                    return;
                                }
                                else
                                {
                                    tempActionMapNames.Add(actionMapName);
                                    tempActionMapLocations[actionMapName] = location;
                                }
                            }
                        }
                    }
                }
            }

            if (_statusMessageType == MessageType.Error && _statusMessage != null && 
                (_statusMessage.Contains("Duplicate") || _statusMessage.Contains("GlobalActions")))
            {
                return;
            }
            
            SetStatus("", MessageType.Info);
        }

        private void RebuildValidationCache()
        {
            if (_configSO == null || _serializedConfig == null) return;

            _cachedContextNames.Clear();
            _cachedActionMapNames.Clear();
            _contextNameToLocation.Clear();
            _actionMapNameToLocation.Clear();

            const string globalActionMap = "GlobalActions";
            _cachedActionMapNames.Add(globalActionMap);
            _actionMapNameToLocation[globalActionMap] = "Global (Join Actions)";

            bool hasError = false;

            var slotsProp = _serializedConfig.FindProperty("_playerSlots");
            if (slotsProp != null && slotsProp.isArray)
            {
                for (int i = 0; i < slotsProp.arraySize; i++)
                {
                    var slotProp = slotsProp.GetArrayElementAtIndex(i);
                    var contextsProp = slotProp.FindPropertyRelative("Contexts");
                    
                    if (contextsProp != null && contextsProp.isArray)
                    {
                        for (int ctxIdx = 0; ctxIdx < contextsProp.arraySize; ctxIdx++)
                        {
                            var ctxProp = contextsProp.GetArrayElementAtIndex(ctxIdx);
                            var ctxNameProp = ctxProp.FindPropertyRelative("Name");
                            var ctxActionMapProp = ctxProp.FindPropertyRelative("ActionMap");
                            
                            if (ctxNameProp != null && !string.IsNullOrEmpty(ctxNameProp.stringValue))
                            {
                                string ctxName = ctxNameProp.stringValue;
                                string location = $"Player {i}, Context {ctxIdx}";
                                
                                if (_cachedContextNames.Contains(ctxName))
                                {
                                    string existingLoc = _contextNameToLocation.TryGetValue(ctxName, out var loc) ? loc : "unknown";
                                    SetStatus($"❌ Duplicate Context name: \"{ctxName}\" at {location} (already used at {existingLoc})", MessageType.Error);
                                    hasError = true;
                                }
                                else
                                {
                                    _cachedContextNames.Add(ctxName);
                                    _contextNameToLocation[ctxName] = location;
                                }
                            }
                            
                            if (ctxActionMapProp != null && !string.IsNullOrEmpty(ctxActionMapProp.stringValue))
                            {
                                string actionMapName = ctxActionMapProp.stringValue;
                                string location = $"Player {i}, Context {ctxIdx}";
                                
                                if (actionMapName == "GlobalActions")
                                {
                                    SetStatus($"❌ ActionMap name \"GlobalActions\" is reserved for Join Actions at {location}. Please use a different name.", MessageType.Error);
                                    hasError = true;
                                }
                                else if (_cachedActionMapNames.Contains(actionMapName))
                                {
                                    string existingLoc = _actionMapNameToLocation.TryGetValue(actionMapName, out var loc) ? loc : "unknown";
                                    SetStatus($"❌ Duplicate ActionMap name: \"{actionMapName}\" at {location} (already used at {existingLoc})", MessageType.Error);
                                    hasError = true;
                                }
                                else
                                {
                                    _cachedActionMapNames.Add(actionMapName);
                                    _actionMapNameToLocation[actionMapName] = location;
                                }
                            }
                        }
                    }
                }
            }

            if (!hasError)
            {
                SetStatus("", MessageType.Info);
            }
        }

        private string GetConfigHash(InputConfiguration config)
        {
            var sb = new StringBuilder();
            if (config.PlayerSlots != null)
            {
                foreach (var slot in config.PlayerSlots)
                {
                    sb.Append($"P{slot.PlayerId}:");
                    if (slot.Contexts != null)
                    {
                        foreach (var ctx in slot.Contexts)
                        {
                            sb.Append($"C[{ctx.Name}]:M[{ctx.ActionMap}]:");
                            if (ctx.Bindings != null)
                            {
                                foreach (var b in ctx.Bindings)
                                {
                                    sb.Append($"A[{b.ActionName}];");
                                }
                            }
                        }
                    }
                }
            }
            return sb.ToString();
        }

        private InputConfiguration CreateDefaultConfigTemplate()
        {
            return new InputConfiguration
            {
                PlayerSlots = new System.Collections.Generic.List<PlayerSlotConfig>
                {
                    new PlayerSlotConfig
                    {
                        PlayerId = 0,
                        JoinAction = new ActionBindingConfig
                        {
                            Type = ActionValueType.Button,
                            ActionName = "JoinGame",
                            DeviceBindings = new System.Collections.Generic.List<string> { "<Keyboard>/enter", "<Gamepad>/start" },
                            LongPressMs = 0
                        },
                        Contexts = new System.Collections.Generic.List<ContextDefinitionConfig>
                        {
                            new ContextDefinitionConfig
                            {
                                Name = "Gameplay",
                                ActionMap = "PlayerActions",
                                Bindings = new System.Collections.Generic.List<ActionBindingConfig>
                                {
                                    new ActionBindingConfig
                                    {
                                        Type = ActionValueType.Vector2,
                                        ActionName = "Move",
                                        DeviceBindings = new System.Collections.Generic.List<string> {
                                            InputBindingConstants.Vector2Sources.Gamepad_LeftStick,
                                            InputBindingConstants.Vector2Sources.Composite_WASD,
                                            InputBindingConstants.Vector2Sources.Mouse_Delta
                                        }
                                    },
                                    new ActionBindingConfig
                                    {
                                        Type = ActionValueType.Button,
                                        ActionName = "Confirm",
                                        DeviceBindings = new System.Collections.Generic.List<string> { "<Gamepad>/buttonSouth", "<Keyboard>/space" },
                                        LongPressMs = 500
                                    }
                                }
                            }
                        }
                    },
                    new PlayerSlotConfig
                    {
                        PlayerId = 1,
                        JoinAction = new ActionBindingConfig
                        {
                            Type = ActionValueType.Button,
                            ActionName = "JoinGame",
                            DeviceBindings = new System.Collections.Generic.List<string> { "<Keyboard>/enter", "<Gamepad>/start" },
                            LongPressMs = 0
                        },
                        Contexts = new System.Collections.Generic.List<ContextDefinitionConfig>
                        {
                             new ContextDefinitionConfig
                            {
                                Name = "Gameplay",
                                ActionMap = "PlayerActions",
                                Bindings = new System.Collections.Generic.List<ActionBindingConfig>
                                {
                                     new ActionBindingConfig
                                     {
                                         Type = ActionValueType.Vector2,
                                         ActionName = "Move",
                                         DeviceBindings = new System.Collections.Generic.List<string> { "<Gamepad>/leftStick", "2DVector(mode=2,up=<Keyboard>/w,down=<Keyboard>/s,left=<Keyboard>/a,right=<Keyboard>/d)" }
                                     },
                                     new ActionBindingConfig
                                     {
                                         Type = ActionValueType.Button,
                                         ActionName = "Confirm",
                                         DeviceBindings = new System.Collections.Generic.List<string> { "<Gamepad>/buttonSouth", "<Keyboard>/space" },
                                         LongPressMs = 500
                                     }
                                }
                            }
                        }
                    }
                }
            };
        }

        private void ClearEditor()
        {
            _configSO = null;
            _serializedConfig = null;
            _validationCacheDirty = true;
            _cachedContextNames.Clear();
            _cachedActionMapNames.Clear();
            _contextNameToLocation.Clear();
            _actionMapNameToLocation.Clear();
            _previousContextCounts.Clear();
        }
        
        private string GenerateUniqueContextName(int playerIndex, string baseName = null)
        {
            if (string.IsNullOrEmpty(baseName))
            {
                baseName = "NewContext";
            }
            
            string candidate = baseName;
            int suffix = 1;
            
            while (_cachedContextNames.Contains(candidate))
            {
                candidate = string.Concat(baseName, suffix.ToString());
                suffix++;
            }
            
            return candidate;
        }
        
        private string GenerateUniqueActionMapName(int playerIndex, string baseName = null)
        {
            if (string.IsNullOrEmpty(baseName))
            {
                baseName = "NewActionMap";
            }
            
            if (baseName == "GlobalActions")
            {
                baseName = "NewActionMap";
            }
            
            string candidate = baseName;
            int suffix = 1;
            
            while (_cachedActionMapNames.Contains(candidate) || candidate == "GlobalActions")
            {
                candidate = string.Concat(baseName, suffix.ToString());
                suffix++;
            }
            
            return candidate;
        }

        private void SetStatus(string message, MessageType type)
        {
            _statusMessage = message;
            _statusMessageType = type;
        }

        private void AddNewPlayer(SerializedProperty slotsProp)
        {
            int newIndex = slotsProp.arraySize;
            slotsProp.arraySize++;
            var newSlot = slotsProp.GetArrayElementAtIndex(newIndex);

            newSlot.FindPropertyRelative("PlayerId").intValue = newIndex;

            var joinAction = newSlot.FindPropertyRelative("JoinAction");
            joinAction.FindPropertyRelative("Type").enumValueIndex = (int)CycloneGames.InputSystem.Runtime.ActionValueType.Button;
            joinAction.FindPropertyRelative("ActionName").stringValue = "JoinGame";
            var joinBindings = joinAction.FindPropertyRelative("DeviceBindings");
            joinBindings.arraySize = 2;
            joinBindings.GetArrayElementAtIndex(0).stringValue = "<Keyboard>/enter";
            joinBindings.GetArrayElementAtIndex(1).stringValue = "<Gamepad>/start";
            joinAction.FindPropertyRelative("LongPressMs").intValue = 0;

            var contexts = newSlot.FindPropertyRelative("Contexts");
            contexts.arraySize = 1;
            var context = contexts.GetArrayElementAtIndex(0);
            context.FindPropertyRelative("Name").stringValue = "Gameplay";
            context.FindPropertyRelative("ActionMap").stringValue = "PlayerActions";

            var bindings = context.FindPropertyRelative("Bindings");
            bindings.arraySize = 2;

            var moveBinding = bindings.GetArrayElementAtIndex(0);
            moveBinding.FindPropertyRelative("Type").enumValueIndex = (int)CycloneGames.InputSystem.Runtime.ActionValueType.Vector2;
            moveBinding.FindPropertyRelative("ActionName").stringValue = "Move";
            var moveDeviceBindings = moveBinding.FindPropertyRelative("DeviceBindings");
            moveDeviceBindings.arraySize = 3;
            moveDeviceBindings.GetArrayElementAtIndex(0).stringValue = "<Gamepad>/leftStick";
            moveDeviceBindings.GetArrayElementAtIndex(1).stringValue = "2DVector(mode=2,up=<Keyboard>/w,down=<Keyboard>/s,left=<Keyboard>/a,right=<Keyboard>/d)";
            moveDeviceBindings.GetArrayElementAtIndex(2).stringValue = "<Mouse>/delta";
            moveBinding.FindPropertyRelative("LongPressMs").intValue = 0;
            moveBinding.FindPropertyRelative("LongPressValueThreshold").floatValue = 0f;

            var confirmBinding = bindings.GetArrayElementAtIndex(1);
            confirmBinding.FindPropertyRelative("Type").enumValueIndex = (int)CycloneGames.InputSystem.Runtime.ActionValueType.Button;
            confirmBinding.FindPropertyRelative("ActionName").stringValue = "Confirm";
            var confirmDeviceBindings = confirmBinding.FindPropertyRelative("DeviceBindings");
            confirmDeviceBindings.arraySize = 2;
            confirmDeviceBindings.GetArrayElementAtIndex(0).stringValue = "<Gamepad>/buttonSouth";
            confirmDeviceBindings.GetArrayElementAtIndex(1).stringValue = "<Keyboard>/space";
            confirmBinding.FindPropertyRelative("LongPressMs").intValue = 500;
            confirmBinding.FindPropertyRelative("LongPressValueThreshold").floatValue = 0f;

            _serializedConfig.ApplyModifiedProperties();
        }

        private void AddNewBinding(SerializedProperty bindingsProp)
        {
            int newIndex = bindingsProp.arraySize;
            bindingsProp.arraySize++;
            var newBinding = bindingsProp.GetArrayElementAtIndex(newIndex);

            newBinding.FindPropertyRelative("Type").enumValueIndex = (int)CycloneGames.InputSystem.Runtime.ActionValueType.Button;
            newBinding.FindPropertyRelative("ActionName").stringValue = "NewAction";
            var deviceBindings = newBinding.FindPropertyRelative("DeviceBindings");
            deviceBindings.arraySize = 1;
            deviceBindings.GetArrayElementAtIndex(0).stringValue = "<Keyboard>/space";
            newBinding.FindPropertyRelative("LongPressMs").intValue = 0;
            newBinding.FindPropertyRelative("LongPressValueThreshold").floatValue = 0.5f;

            _serializedConfig.ApplyModifiedProperties();
        }
    }
}