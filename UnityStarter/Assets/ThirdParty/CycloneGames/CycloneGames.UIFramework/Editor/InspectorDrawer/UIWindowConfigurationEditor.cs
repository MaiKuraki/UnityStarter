// Copyright (c) CycloneGames
// Licensed under the MIT License.

using UnityEditor;
using UnityEngine;
using CycloneGames.UIFramework.Runtime;
using CycloneGames.AssetManagement.Runtime;

namespace CycloneGames.UIFramework.Editor
{
    [CustomEditor(typeof(UIWindowConfiguration))]
    public sealed class UIWindowConfigurationEditor : UnityEditor.Editor
    {
        // Duplicate detection
        private static string[] allConfigGuids;
        private static bool hasCheckedForDuplicates;

        // Colors for visual styling
        private static readonly Color headerColor = new Color(0.3f, 0.5f, 0.8f);
        private static readonly Color prefabRefColor = new Color(0.4f, 0.7f, 0.4f);
        private static readonly Color locationColor = new Color(0.8f, 0.6f, 0.3f);
        private static readonly Color priorityColor = new Color(0.6f, 0.4f, 0.8f);
        private static readonly Color warningColor = new Color(0.95f, 0.7f, 0.2f);
        private static readonly Color errorColor = new Color(0.9f, 0.3f, 0.3f);
        private static readonly Color successColor = new Color(0.3f, 0.8f, 0.4f);

        // Serialized properties
        private SerializedProperty sourceProp;
        private SerializedProperty windowPrefabProp;
        private SerializedProperty prefabLocationProp;
        private SerializedProperty layerProp;
        private SerializedProperty priorityProp;

        // Foldout states
        private bool showPrefabSource = true;
        private bool showLayerSettings = true;

        // Cached GUIStyles to avoid per-frame allocations
        private GUIStyle _titleStyle;
        private GUIStyle _subtitleStyle;
        private GUIStyle _statusBoxStyle;
        private GUIStyle _foldoutLabelStyle;
        private bool _stylesInitialized;

        private void OnEnable()
        {
            sourceProp = serializedObject.FindProperty("source");
            windowPrefabProp = serializedObject.FindProperty("windowPrefab");
            prefabLocationProp = serializedObject.FindProperty("prefabLocation");
            layerProp = serializedObject.FindProperty("layer");
            priorityProp = serializedObject.FindProperty("priority");

            hasCheckedForDuplicates = false;
            _stylesInitialized = false;
        }

        private void InitializeStyles()
        {
            if (_stylesInitialized) return;
            _stylesInitialized = true;

            _titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter
            };

            _subtitleStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                fontSize = 10
            };

            _statusBoxStyle = new GUIStyle(EditorStyles.label)
            {
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            _foldoutLabelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = Color.white },
                alignment = TextAnchor.MiddleLeft
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Initialize cached styles
            InitializeStyles();

            // Check for duplicates with same name pattern
            CheckForDuplicates();

            var config = (UIWindowConfiguration)target;

            // Title
            DrawTitle(config);

            EditorGUILayout.Space(5);

            // Validation summary
            DrawValidationSummary(config);

            EditorGUILayout.Space(5);

            // Layer & Priority Section
            showLayerSettings = DrawFoldoutHeader("Layer & Priority", showLayerSettings, headerColor);
            if (showLayerSettings)
            {
                DrawLayerSection(config);
            }

            EditorGUILayout.Space(3);

            // Prefab Source Section
            showPrefabSource = DrawFoldoutHeader("Prefab Source", showPrefabSource, 
                config.Source == UIWindowConfiguration.PrefabSource.PrefabReference ? prefabRefColor : locationColor);
            if (showPrefabSource)
            {
                DrawPrefabSourceSection(config);
            }

            EditorGUILayout.Space(10);

            // Quick Actions
            DrawQuickActions(config);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawTitle(UIWindowConfiguration config)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            EditorGUILayout.LabelField("UI Window Configuration", _titleStyle, GUILayout.Height(24));

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            // Subtitle with asset name
            EditorGUILayout.LabelField(config.name, _subtitleStyle);
        }

        private void DrawValidationSummary(UIWindowConfiguration config)
        {
            bool hasLayer = config.Layer != null;
            bool hasPrefab = config.Source == UIWindowConfiguration.PrefabSource.PrefabReference 
                ? config.WindowPrefab != null 
                : !string.IsNullOrEmpty(config.PrefabLocation);

            if (hasLayer && hasPrefab)
            {
                // All good - show success indicator
                DrawStatusBox("[OK] Configuration Valid", successColor, MessageType.None);
            }
            else
            {
                // Show warnings
                if (!hasLayer)
                {
                    EditorGUILayout.HelpBox("Layer is not assigned. This window won't be placed on any layer.", MessageType.Warning);
                }
                if (!hasPrefab)
                {
                    string msg = config.Source == UIWindowConfiguration.PrefabSource.PrefabReference
                        ? "Window Prefab is not assigned."
                        : "Prefab Location is empty.";
                    EditorGUILayout.HelpBox(msg, MessageType.Warning);
                }
            }
        }

        private void DrawStatusBox(string message, Color color, MessageType type)
        {
            Color oldBg = GUI.backgroundColor;
            GUI.backgroundColor = color;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = oldBg;

            EditorGUILayout.LabelField(message, _statusBoxStyle);

            EditorGUILayout.EndVertical();
        }

        private void DrawLayerSection(UIWindowConfiguration config)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Layer field
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Layer", GUILayout.Width(80));
            EditorGUILayout.PropertyField(layerProp, GUIContent.none);
            EditorGUILayout.EndHorizontal();

            if (config.Layer != null)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.LabelField($"Layer: {config.Layer.LayerName}", EditorStyles.miniLabel);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(5);

            // Priority with visual slider
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Priority", GUILayout.Width(80));

            // Color-coded priority visualization
            int priority = priorityProp.intValue;
            Color priorityDisplayColor = GetPriorityColor(priority);

            Color oldColor = GUI.color;
            GUI.color = priorityDisplayColor;
            EditorGUILayout.IntSlider(priorityProp, -100, 400, GUIContent.none);
            GUI.color = oldColor;

            EditorGUILayout.EndHorizontal();

            // Priority explanation
            string priorityHint = GetPriorityHint(priority);
            EditorGUILayout.LabelField(priorityHint, EditorStyles.centeredGreyMiniLabel);

            EditorGUILayout.Space(3);

            // Detailed Priority explanation
            EditorGUILayout.HelpBox(
                "Priority determines render order within the same Layer.\n" +
                "• Higher value = rendered on top (closer to camera)\n" +
                "• Lower value = rendered behind other windows\n" +
                "• Windows with same priority: order depends on open sequence",
                MessageType.None);

            EditorGUILayout.EndVertical();
        }

        private Color GetPriorityColor(int priority)
        {
            if (priority < 0) return new Color(0.5f, 0.5f, 0.8f); // Low priority - blue
            if (priority < 100) return Color.white;                // Normal - white
            if (priority < 200) return new Color(0.9f, 0.8f, 0.3f); // Above normal - yellow
            return new Color(0.9f, 0.4f, 0.3f);                    // High priority - red/orange
        }

        private string GetPriorityHint(int priority)
        {
            if (priority < 0) return "<< Below Default (Background)";
            if (priority == 0) return "-- Default Priority";
            if (priority < 100) return ">> Above Default";
            if (priority < 200) return ">>> High Priority (Modal)";
            if (priority < 300) return ">>>> Very High (Overlay)";
            return ">>>>> Maximum Priority (System)";
        }

        private void DrawPrefabSourceSection(UIWindowConfiguration config)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // Source toggle with visual indication
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Source Mode", GUILayout.Width(100));

            var source = (UIWindowConfiguration.PrefabSource)sourceProp.enumValueIndex;

            // Custom toggle buttons
            Color oldBg = GUI.backgroundColor;

            GUI.backgroundColor = source == UIWindowConfiguration.PrefabSource.PrefabReference 
                ? prefabRefColor : Color.gray;
            if (GUILayout.Button("Direct Reference", EditorStyles.miniButtonLeft))
            {
                sourceProp.enumValueIndex = (int)UIWindowConfiguration.PrefabSource.PrefabReference;
            }

            GUI.backgroundColor = source == UIWindowConfiguration.PrefabSource.Location 
                ? locationColor : Color.gray;
            if (GUILayout.Button("Asset Location", EditorStyles.miniButtonRight))
            {
                sourceProp.enumValueIndex = (int)UIWindowConfiguration.PrefabSource.Location;
            }

            GUI.backgroundColor = oldBg;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            // Conditional fields based on source
            if (source == UIWindowConfiguration.PrefabSource.PrefabReference)
            {
                DrawPrefabReferenceField(config);
            }
            else
            {
                DrawLocationField(config);
            }

            EditorGUILayout.EndVertical();

            // Help text
            string helpText = source == UIWindowConfiguration.PrefabSource.PrefabReference
                ? "Direct Reference: Prefab is included in build. Best for always-used windows."
                : "Asset Location: Prefab loaded dynamically via AssetManagement. Best for on-demand windows.";
            EditorGUILayout.HelpBox(helpText, MessageType.Info);
        }

        private void DrawPrefabReferenceField(UIWindowConfiguration config)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Window Prefab", GUILayout.Width(100));
            EditorGUILayout.PropertyField(windowPrefabProp, GUIContent.none);
            EditorGUILayout.EndHorizontal();

            // Validation
            if (config.WindowPrefab != null)
            {
                var uiWindow = config.WindowPrefab.GetComponent<UIWindow>();
                if (uiWindow == null)
                {
                    EditorGUILayout.HelpBox("[ERROR] Prefab must have a UIWindow component!", MessageType.Error);
                }
                else
                {
                    EditorGUILayout.LabelField($"[OK] UIWindow: {config.WindowPrefab.name}", EditorStyles.miniLabel);
                }
            }
        }

        private void DrawLocationField(UIWindowConfiguration config)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Location", GUILayout.Width(100));
            EditorGUILayout.PropertyField(prefabLocationProp, GUIContent.none);
            EditorGUILayout.EndHorizontal();

            // Validate button
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Validate Location", GUILayout.Width(130)))
            {
                ValidateLocation(config);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawQuickActions(UIWindowConfiguration config)
        {
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Copy Window Name"))
            {
                string windowName = config.name.Replace("UIWindow_", "");
                EditorGUIUtility.systemCopyBuffer = windowName;
                Debug.Log($"[UIWindowConfiguration] Copied window name: {windowName}");
            }

            if (config.WindowPrefab != null)
            {
                if (GUILayout.Button("Select Prefab"))
                {
                    Selection.activeObject = config.WindowPrefab;
                    EditorGUIUtility.PingObject(config.WindowPrefab);
                }
            }

            if (config.Layer != null)
            {
                if (GUILayout.Button("Select Layer"))
                {
                    Selection.activeObject = config.Layer;
                    EditorGUIUtility.PingObject(config.Layer);
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        #region Utility Methods

        private void CheckForDuplicates()
        {
            if (!hasCheckedForDuplicates || Event.current.type == EventType.Layout)
            {
                allConfigGuids = AssetDatabase.FindAssets("t:UIWindowConfiguration");
                hasCheckedForDuplicates = true;
            }
        }

        private bool DrawFoldoutHeader(string title, bool foldout, Color color)
        {
            EditorGUILayout.Space(2);

            Rect rect = EditorGUILayout.GetControlRect(false, 22);

            // Background
            Color bgColor = foldout ? color : new Color(color.r * 0.7f, color.g * 0.7f, color.b * 0.7f);
            EditorGUI.DrawRect(rect, bgColor);

            // Border
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1), Color.black * 0.2f);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), Color.black * 0.2f);

            // Label - use cached style
            Rect labelRect = new Rect(rect.x + 20, rect.y, rect.width - 20, rect.height);
            EditorGUI.LabelField(labelRect, title, _foldoutLabelStyle);

            // Arrow
            string arrow = foldout ? "v" : ">";
            Rect arrowRect = new Rect(rect.x + 5, rect.y, 15, rect.height);
            EditorGUI.LabelField(arrowRect, arrow, _foldoutLabelStyle);

            // Click handling
            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                foldout = !foldout;
                Event.current.Use();
            }

            return foldout;
        }

        private void ValidateLocation(UIWindowConfiguration config)
        {
            if (string.IsNullOrEmpty(config.PrefabLocation))
            {
                EditorUtility.DisplayDialog("UIWindowConfiguration", "PrefabLocation is empty.", "OK");
                return;
            }

            var pkg = AssetManagementLocator.DefaultPackage;
            if (pkg == null)
            {
                EditorUtility.DisplayDialog("UIWindowConfiguration", 
                    "DefaultPackage is null.\n\nInitialize AssetManagement and assign DefaultPackage first.", "OK");
                return;
            }

            var handle = pkg.LoadAssetAsync<GameObject>(config.PrefabLocation);
            EditorApplication.delayCall += () =>
            {
                if (!handle.IsDone)
                {
                    EditorApplication.delayCall += () => FinalizeValidation(handle, config.PrefabLocation);
                }
                else
                {
                    FinalizeValidation(handle, config.PrefabLocation);
                }
            };
        }

        private void FinalizeValidation(IAssetHandle<GameObject> handle, string location)
        {
            if (handle == null)
            {
                EditorUtility.DisplayDialog("UIWindowConfiguration", "Handle null during validation.", "OK");
                return;
            }

            bool success = string.IsNullOrEmpty(handle.Error) && handle.Asset != null;
            string icon = success ? "✓" : "✗";
            string msg = success
                ? $"{icon} Location Valid\n\nPath: {location}\nPrefab: {handle.Asset?.name}"
                : $"{icon} Location Invalid\n\nPath: {location}\nError: {handle.Error}";

            handle.Dispose();
            EditorUtility.DisplayDialog("Validation Result", msg, "OK");
        }

        #endregion
    }
}