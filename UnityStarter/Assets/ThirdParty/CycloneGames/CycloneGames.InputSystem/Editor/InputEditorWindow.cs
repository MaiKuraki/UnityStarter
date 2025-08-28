using UnityEngine;
using UnityEditor;
using System.IO;
using VYaml.Serialization;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CycloneGames.Utility.Runtime;
using CycloneGames.InputSystem.Runtime;

namespace CycloneGames.InputSystem.Editor
{
    public class InputEditorWindow : EditorWindow
    {
        // --- Private Fields ---
        private InputConfigurationSO _configSO;
        private SerializedObject _serializedConfig;
        private Vector2 _scrollPosition;
        private GUIStyle _overflowLabelStyle;

        private string _defaultConfigPath;
        private string _userConfigPath;
        private string _statusMessage;
        private MessageType _statusMessageType = MessageType.Info;

        // --- Code Generation Settings ---
        private string _codegenPath;
        private string _codegenNamespace;
        private DefaultAsset _codegenFolder;

        // --- Constants ---
        private const string DefaultConfigFileName = "input_config.yaml";
        private const string UserConfigFileName = "user_input_settings.yaml";

        [MenuItem("Tools/CycloneGames/Input System Editor")]
        public static void ShowWindow()
        {
            GetWindow<InputEditorWindow>("Input System Editor");
        }

        private void OnEnable()
        {
            _defaultConfigPath = FilePathUtility.GetUnityWebRequestUri(DefaultConfigFileName, UnityPathSource.StreamingAssets);
            _userConfigPath = FilePathUtility.GetUnityWebRequestUri(UserConfigFileName, UnityPathSource.PersistentData);
            
            // Load codegen settings
            _codegenPath = EditorPrefs.GetString("CycloneGames.InputSystem.CodegenPath", "Assets");
            _codegenNamespace = EditorPrefs.GetString("CycloneGames.InputSystem.CodegenNamespace", "YourGame.Input.Generated");
            if (!string.IsNullOrEmpty(_codegenPath))
            {
                _codegenFolder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(_codegenPath);
            }
            
            LoadUserConfig();
        }

        private void OnGUI()
        {
            if (_overflowLabelStyle == null)
            {
                _overflowLabelStyle = new GUIStyle(EditorStyles.label)
                {
                    clipping = TextClipping.Overflow,
                    wordWrap = false,
                    alignment = TextAnchor.MiddleLeft
                };
            }
            DrawToolbar();
            DrawStatusBar();
            
            DrawCodegenSettings();

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            if (_serializedConfig != null && _configSO != null)
            {
                _serializedConfig.Update();

                // Draw player slots with dynamic add/remove support
                var slotsProp = _serializedConfig.FindProperty("_playerSlots");
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Player Slots", EditorStyles.boldLabel);
                if (GUILayout.Button("+ Add Player", GUILayout.Width(100)))
                {
                    AddNewPlayer(slotsProp);
                }
                EditorGUILayout.EndHorizontal();

                // Draw player slots with conditional UI for long-press fields
                if (slotsProp.arraySize > 0)
                {
                    for (int i = 0; i < slotsProp.arraySize; i++)
                    {
                        var slotProp = slotsProp.GetArrayElementAtIndex(i);
                        
                        // Player header with remove button
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField($"Player {i}", EditorStyles.boldLabel);
                        if (GUILayout.Button("Remove", GUILayout.Width(60)))
                        {
                            slotsProp.DeleteArrayElementAtIndex(i);
                            break;
                        }
                        EditorGUILayout.EndHorizontal();
                        
                        EditorGUI.indentLevel++;
                        
                        // Player ID
                        EditorGUILayout.PropertyField(slotProp.FindPropertyRelative("PlayerId"));
                        
                        // Join Action for this player (always visible)
                        EditorGUILayout.LabelField("Join Action", EditorStyles.boldLabel);
                        EditorGUI.indentLevel++;
                        var joinTypeProp = slotProp.FindPropertyRelative("JoinAction.Type");
                        var joinActionProp = slotProp.FindPropertyRelative("JoinAction.ActionName");
                        var joinBindingsProp = slotProp.FindPropertyRelative("JoinAction.DeviceBindings");
                        var joinLongPressProp = slotProp.FindPropertyRelative("JoinAction.LongPressMs");
                        
                        EditorGUILayout.PropertyField(joinTypeProp);
                        EditorGUILayout.PropertyField(joinActionProp);
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
                        
                        // Contexts (collapsible)
                        var contextsProp = slotProp.FindPropertyRelative("Contexts");
                        EditorGUILayout.PropertyField(contextsProp, new GUIContent("Contexts"), true);
                        EditorGUI.indentLevel--;
                        
                        // Add separator between players
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
            }
            else
            {
                EditorGUILayout.HelpBox("No configuration loaded. Generate or load a configuration file using the toolbar.", MessageType.Warning);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("Load User Config", EditorStyles.toolbarButton)) LoadUserConfig();
            if (GUILayout.Button("Load Default Config", EditorStyles.toolbarButton)) LoadDefaultConfig();
            GUILayout.Space(10);
            if (GUILayout.Button("Generate Default Config", EditorStyles.toolbarButton)) GenerateDefaultConfigFile();
            GUILayout.Space(20);
            GUI.enabled = _configSO != null;
            if (GUILayout.Button("Save to User Config", EditorStyles.toolbarButton)) SaveChangesToUserConfig();
            if (GUILayout.Button("Save and Generate Constants", EditorStyles.toolbarButton))
            {
                SaveChangesToUserConfig(true); // Pass true to trigger generation
            }
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Reset User to Default", EditorStyles.toolbarButton))
            {
                if (EditorUtility.DisplayDialog("Reset User Configuration?", "This will overwrite your user settings with the default configuration. This cannot be undone.", "Reset", "Cancel"))
                {
                    ResetToDefault();
                }
            }
            EditorGUILayout.EndHorizontal();
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
                byte[] yamlBytes = YamlSerializer.Serialize(configModel).ToArray();
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

        /// <summary>
        /// Generates a new default configuration file with a standard template.
        /// </summary>
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
                byte[] yamlBytes = YamlSerializer.Serialize(defaultConfig).ToArray();
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
        
        private void GenerateConstantsFile(InputConfiguration config)
        {
            var actionMaps = new HashSet<string>();
            var actions = new HashSet<string>();

            if (config.PlayerSlots != null)
            {
                foreach (var slot in config.PlayerSlots)
                {
                    if (slot.JoinAction != null && !string.IsNullOrEmpty(slot.JoinAction.ActionName))
                    {
                        actions.Add(slot.JoinAction.ActionName);
                    }

                    if (slot.Contexts != null)
                    {
                        foreach (var context in slot.Contexts)
                        {
                            if (!string.IsNullOrEmpty(context.ActionMap))
                            {
                                actionMaps.Add(context.ActionMap);
                            }

                            if (context.Bindings != null)
                            {
                                foreach (var binding in context.Bindings)
                                {
                                    if (!string.IsNullOrEmpty(binding.ActionName))
                                    {
                                        actions.Add(binding.ActionName);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            
            if (config.JoinAction != null && !string.IsNullOrEmpty(config.JoinAction.ActionName))
            {
                actions.Add(config.JoinAction.ActionName);
            }

            var sb = new StringBuilder();
            sb.AppendLine("// -- AUTO-GENERATED FILE --");
            sb.AppendLine("// This file is generated by the CycloneGames.InputSystem Editor window.");
            sb.AppendLine("// Do not modify this file manually.");
            sb.AppendLine();
            sb.AppendLine($"namespace {_codegenNamespace}");
            sb.AppendLine("{");
            sb.AppendLine("    public static class InputActions");
            sb.AppendLine("    {");

            // --- ActionMaps Class ---
            sb.AppendLine("        public static class ActionMaps");
            sb.AppendLine("        {");
            foreach (var map in actionMaps.OrderBy(a => a))
            {
                sb.AppendLine($"            public static readonly int {SanitizeIdentifier(map)} = \"{map}\".GetHashCode();");
            }
            sb.AppendLine("        }");
            sb.AppendLine();

            // --- Actions Class (now with context) ---
            sb.AppendLine("        public static class Actions");
            sb.AppendLine("        {");
            if (config.PlayerSlots != null)
            {
                var allBindings = config.PlayerSlots
                    .Where(slot => slot.Contexts != null)
                    .SelectMany(slot => slot.Contexts)
                    .Where(ctx => ctx.Bindings != null && !string.IsNullOrEmpty(ctx.Name))
                    .SelectMany(ctx => ctx.Bindings.Select(b => new { Context = ctx.Name, Action = b.ActionName, Map = ctx.ActionMap }))
                    .Distinct();

                foreach (var binding in allBindings.OrderBy(b => b.Context).ThenBy(b => b.Action))
                {
                    if (string.IsNullOrEmpty(binding.Action)) continue;
                    
                    // The constant name is Context_Action for intuitive use in code.
                    string constantName = $"{binding.Context}_{binding.Action}";
                    // The ID is based on Map/Action, as the runtime InputActionAsset is structured by maps.
                    string uniqueId = $"{binding.Map}/{binding.Action}";
                    sb.AppendLine($"            public static readonly int {SanitizeIdentifier(constantName)} = \"{uniqueId}\".GetHashCode();");
                }
            }
            // Also handle player-specific join actions
            if (config.PlayerSlots != null)
            {
                foreach (var slot in config.PlayerSlots.Where(s => s.JoinAction != null && !string.IsNullOrEmpty(s.JoinAction.ActionName)))
                {
                    // We'll invent a context name for these for clarity
                    const string joinContext = "PlayerJoin";
                    // And a map name
                    const string joinMap = "GlobalActions";
                    string constantName = $"{joinContext}_P{slot.PlayerId}_{slot.JoinAction.ActionName}";
                    string uniqueId = $"{joinMap}/{slot.JoinAction.ActionName}";
                     sb.AppendLine($"            public static readonly int {SanitizeIdentifier(constantName)} = \"{uniqueId}\".GetHashCode();");
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
            
            // Handle first character
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

            // Handle remaining characters
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

        /// <summary>
        /// Creates a hardcoded template for a new default configuration.
        /// </summary>
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
            
            // Set default values for new player
            newSlot.FindPropertyRelative("PlayerId").intValue = newIndex;
            
            // Add default join action
            var joinAction = newSlot.FindPropertyRelative("JoinAction");
            joinAction.FindPropertyRelative("Type").enumValueIndex = (int)CycloneGames.InputSystem.Runtime.ActionValueType.Button;
            joinAction.FindPropertyRelative("ActionName").stringValue = "JoinGame";
            var joinBindings = joinAction.FindPropertyRelative("DeviceBindings");
            joinBindings.arraySize = 2;
            joinBindings.GetArrayElementAtIndex(0).stringValue = "<Keyboard>/enter";
            joinBindings.GetArrayElementAtIndex(1).stringValue = "<Gamepad>/start";
            joinAction.FindPropertyRelative("LongPressMs").intValue = 0;
            
            // Add default context
            var contexts = newSlot.FindPropertyRelative("Contexts");
            contexts.arraySize = 1;
            var context = contexts.GetArrayElementAtIndex(0);
            context.FindPropertyRelative("Name").stringValue = "Gameplay";
            context.FindPropertyRelative("ActionMap").stringValue = "PlayerActions";
            
            // Add default bindings
            var bindings = context.FindPropertyRelative("Bindings");
            bindings.arraySize = 2;
            
            // Move binding
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
            
            // Confirm binding
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
            
            // Set default values for new binding
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
