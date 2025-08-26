using UnityEngine;
using UnityEditor;
using System.IO;
using VYaml.Serialization;
using System.Collections.Generic;
using System.Linq;
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

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            if (_serializedConfig != null && _configSO != null)
            {
                _serializedConfig.Update();

                // Draw join action
                EditorGUILayout.PropertyField(_serializedConfig.FindProperty("_joinAction"), true);

                // Draw player slots with conditional UI for long-press fields
                var slotsProp = _serializedConfig.FindProperty("_playerSlots");
                EditorGUILayout.PropertyField(slotsProp, new GUIContent("Player Slots"), false);
                if (slotsProp.isExpanded)
                {
                    EditorGUI.indentLevel++;
                    for (int i = 0; i < slotsProp.arraySize; i++)
                    {
                        var slotProp = slotsProp.GetArrayElementAtIndex(i);
                        EditorGUILayout.PropertyField(slotProp, new GUIContent($"Player Slot {i}"), false);
                        if (slotProp.isExpanded)
                        {
                            EditorGUI.indentLevel++;
                            var contextsProp = slotProp.FindPropertyRelative("Contexts");
                            EditorGUILayout.PropertyField(contextsProp, new GUIContent("Contexts"), false);
                            if (contextsProp.isExpanded)
                            {
                                EditorGUI.indentLevel++;
                                for (int c = 0; c < contextsProp.arraySize; c++)
                                {
                                    var ctxProp = contextsProp.GetArrayElementAtIndex(c);
                                    EditorGUILayout.PropertyField(ctxProp.FindPropertyRelative("Name"));
                                    EditorGUILayout.PropertyField(ctxProp.FindPropertyRelative("ActionMap"));

                                    var bindingsProp = ctxProp.FindPropertyRelative("Bindings");
                                    EditorGUILayout.PropertyField(bindingsProp, new GUIContent("Bindings"), false);
                                    if (bindingsProp.isExpanded)
                                    {
                                        EditorGUI.indentLevel++;
                                        for (int b = 0; b < bindingsProp.arraySize; b++)
                                        {
                                            var bindProp = bindingsProp.GetArrayElementAtIndex(b);
                                            var typeProp = bindProp.FindPropertyRelative("Type");
                                            EditorGUILayout.PropertyField(typeProp);
                                            EditorGUILayout.PropertyField(bindProp.FindPropertyRelative("ActionName"));
                                            var type = (CycloneGames.InputSystem.Runtime.ActionValueType)typeProp.enumValueIndex;
                                            // Device bindings with hint for Vector2 sources
                                            if (type == CycloneGames.InputSystem.Runtime.ActionValueType.Vector2)
                                            {
                                                EditorGUILayout.HelpBox("Tip: Vector2 sources include Mouse Delta, Sticks, DPad, or 2DVector composites.", MessageType.None);
                                            }
                                            EditorGUILayout.PropertyField(bindProp.FindPropertyRelative("DeviceBindings"), true);
                                            if (type == CycloneGames.InputSystem.Runtime.ActionValueType.Button)
                                            {
                                                var msProp = bindProp.FindPropertyRelative("LongPressMs");
                                                EditorGUILayout.BeginHorizontal();
                                                EditorGUILayout.LabelField("Long Press (ms)", _overflowLabelStyle, GUILayout.Width(220));
                                                EditorGUILayout.PropertyField(msProp, GUIContent.none, true);
                                                EditorGUILayout.EndHorizontal();
                                            }
                                            else if (type == CycloneGames.InputSystem.Runtime.ActionValueType.Float)
                                            {
                                                var msProp = bindProp.FindPropertyRelative("LongPressMs");
                                                var thProp = bindProp.FindPropertyRelative("LongPressValueThreshold");
                                                EditorGUILayout.BeginHorizontal();
                                                EditorGUILayout.LabelField("Long Press (ms)", _overflowLabelStyle, GUILayout.Width(220));
                                                EditorGUILayout.PropertyField(msProp, GUIContent.none, true);
                                                EditorGUILayout.EndHorizontal();
                                                EditorGUILayout.BeginHorizontal();
                                                EditorGUILayout.LabelField("Long Press Threshold (0-1)", _overflowLabelStyle, GUILayout.Width(220));
                                                EditorGUILayout.PropertyField(thProp, GUIContent.none, true);
                                                EditorGUILayout.EndHorizontal();
                                            }
                                            else
                                            {
                                                using (new EditorGUI.DisabledScope(true))
                                                {
                                                    var tmp = bindProp.FindPropertyRelative("LongPressMs");
                                                    EditorGUILayout.IntField(new GUIContent("LongPressMs"), 0);
                                                }
                                            }
                                        }
                                        EditorGUI.indentLevel--;
                                    }
                                }
                                EditorGUI.indentLevel--;
                            }
                            EditorGUI.indentLevel--;
                        }
                    }
                    EditorGUI.indentLevel--;
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

        private void SaveChangesToUserConfig()
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
                EditorUtility.DisplayDialog("Save Successful", "User input configuration has been saved.", "OK");
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

        /// <summary>
        /// Creates a hardcoded template for a new default configuration.
        /// </summary>
        private InputConfiguration CreateDefaultConfigTemplate()
        {
            return new InputConfiguration
            {
                JoinAction = new ActionBindingConfig
                {
                    Type = ActionValueType.Button,
                    ActionName = "JoinGame",
                    DeviceBindings = new System.Collections.Generic.List<string> { "<Keyboard>/enter", "<Gamepad>/start" },
                    LongPressMs = 0
                },
                PlayerSlots = new System.Collections.Generic.List<PlayerSlotConfig>
                {
                    new PlayerSlotConfig
                    {
                        PlayerId = 0,
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
    }
}