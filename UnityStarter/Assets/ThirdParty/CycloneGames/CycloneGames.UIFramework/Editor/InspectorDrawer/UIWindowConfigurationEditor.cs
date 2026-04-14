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
        // ── Duplicate detection ────────────────────────────────────────────────────────────
        private static string[] allConfigGuids;
        private static bool hasCheckedForDuplicates;

        // ── Colors ────────────────────────────────────────────────────────────────────────
        private static readonly Color headerColor        = new Color(0.3f, 0.5f,  0.8f);
        private static readonly Color prefabRefColor     = new Color(0.4f, 0.7f,  0.4f);   // green  direct / built-in
        private static readonly Color assetRefColor      = new Color(0.35f, 0.55f, 0.85f); // blue   AssetManagement
        private static readonly Color locationColor      = new Color(0.8f, 0.6f,  0.3f);   // orange raw path
        private static readonly Color warningColor       = new Color(0.95f, 0.7f, 0.2f);
        private static readonly Color errorColor         = new Color(0.9f, 0.3f,  0.3f);
        private static readonly Color successColor       = new Color(0.3f, 0.8f,  0.4f);
        private static readonly Color sceneBoundOnColor  = new Color(0.25f, 0.65f, 0.45f); // teal-green (scene-bound)
        private static readonly Color sceneBoundOffColor = new Color(0.45f, 0.45f, 0.45f); // grey       (persistent)
        private const float SourceFieldLabelWidth = 72f;

        // ── Serialized properties ─────────────────────────────────────────────────────────
        private SerializedProperty sourceProp;
        private SerializedProperty windowPrefabProp;
        private SerializedProperty prefabAssetRefProp;   // compound struct
        private SerializedProperty prefabLocationProp;
        private SerializedProperty layerProp;
        private SerializedProperty priorityProp;
        private SerializedProperty isSceneBoundProp;

        // ── Foldout states ────────────────────────────────────────────────────────────────
        private bool showPrefabSource        = true;
        private bool showLayerSettings       = true;
        private bool showSceneBindingSection = true;

        // ── Cached GUIStyles (avoid per-frame allocation) ─────────────────────────────────
        private GUIStyle _titleStyle;
        private GUIStyle _subtitleStyle;
        private GUIStyle _statusBoxStyle;
        private GUIStyle _foldoutLabelStyle;
        private GUIStyle _tagLabelStyle;
        private bool _stylesInitialized;
        private string _migrationStatusMessage;
        private MessageType _migrationStatusType;

        // ── Lifecycle ─────────────────────────────────────────────────────────────────────

        private void OnEnable()
        {
            sourceProp         = serializedObject.FindProperty("source");
            windowPrefabProp   = serializedObject.FindProperty("windowPrefab");
            prefabAssetRefProp = serializedObject.FindProperty("prefabAssetRef");
            prefabLocationProp = serializedObject.FindProperty("prefabLocation");
            layerProp          = serializedObject.FindProperty("layer");
            priorityProp       = serializedObject.FindProperty("priority");
            isSceneBoundProp   = serializedObject.FindProperty("isSceneBound");

            hasCheckedForDuplicates = false;
            _stylesInitialized      = false;
        }

        private void InitializeStyles()
        {
            if (_stylesInitialized) return;
            _stylesInitialized = true;

            _titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize  = 14,
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
                normal    = { textColor = Color.white },
                alignment = TextAnchor.MiddleLeft
            };

            _tagLabelStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = Color.white }
            };
        }

        // ── Main draw ─────────────────────────────────────────────────────────────────────

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            InitializeStyles();
            CheckForDuplicates();

            var config = (UIWindowConfiguration)target;

            DrawTitle(config);
            EditorGUILayout.Space(5);
            DrawValidationSummary(config);
            EditorGUILayout.Space(5);

            showLayerSettings = DrawFoldoutHeader("Layer & Priority", showLayerSettings, headerColor);
            if (showLayerSettings) DrawLayerSection(config);

            EditorGUILayout.Space(3);

            Color sceneBindingColor = config.IsSceneBound ? sceneBoundOnColor : sceneBoundOffColor;
            showSceneBindingSection = DrawFoldoutHeader("Scene Binding", showSceneBindingSection, sceneBindingColor);
            if (showSceneBindingSection) DrawSceneBindingSection(config);

            EditorGUILayout.Space(3);

            Color sourceFoldoutColor = GetSourceColor(config.Source);
            showPrefabSource = DrawFoldoutHeader("Prefab Source", showPrefabSource, sourceFoldoutColor);
            if (showPrefabSource) DrawPrefabSourceSection(config);

            EditorGUILayout.Space(10);
            DrawQuickActions(config);

            serializedObject.ApplyModifiedProperties();
        }

        // ── Title ─────────────────────────────────────────────────────────────────────────

        private void DrawTitle(UIWindowConfiguration config)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("UI Window Configuration", _titleStyle, GUILayout.Height(24));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(config.name, _subtitleStyle);
        }

        // ── Validation summary ────────────────────────────────────────────────────────────

        private void DrawValidationSummary(UIWindowConfiguration config)
        {
            bool hasLayer = config.Layer != null;
            bool hasSource = config.IsConfigured;

            if (hasLayer && hasSource)
            {
                DrawStatusBox("[OK] Configuration Valid", successColor);
            }
            else
            {
                if (!hasLayer)
                    EditorGUILayout.HelpBox("Layer is not assigned. This window won't be placed on any layer.", MessageType.Warning);

                if (!hasSource)
                {
                    string msg = config.Source switch
                    {
                        UIWindowConfiguration.PrefabSource.PrefabReference => "Window Prefab is not assigned.",
                        UIWindowConfiguration.PrefabSource.AssetReference  => "PrefabAssetRef Location is empty.",
                        UIWindowConfiguration.PrefabSource.PathLocation    => "Prefab Path is empty.",
                        _                                                   => "Prefab source is not configured."
                    };
                    EditorGUILayout.HelpBox(msg, MessageType.Warning);
                }
            }
        }

        private void DrawStatusBox(string message, Color color)
        {
            Color oldBg = GUI.backgroundColor;
            GUI.backgroundColor = color;
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.backgroundColor = oldBg;
            EditorGUILayout.LabelField(message, _statusBoxStyle);
            EditorGUILayout.EndVertical();
        }

        // ── Scene Binding section ─────────────────────────────────────────────────────────

        /// <summary>
        /// Draws the Scene Binding toggle that controls <see cref="UIWindowConfiguration.IsSceneBound"/>.
        /// When enabled the window is automatically closed by UIManager whenever the active scene changes.
        /// Persistent windows (HUD, global overlays) should keep this disabled.
        /// </summary>
        private void DrawSceneBindingSection(UIWindowConfiguration config)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // ── Status badge ─────────────────────────────────────────────────
            if (config.IsSceneBound)
                DrawStatusBox("[Scene Bound]  Auto-closes on scene change", sceneBoundOnColor);
            else
                DrawStatusBox("[Persistent]  Survives scene transitions", sceneBoundOffColor);

            EditorGUILayout.Space(4);

            // ── Toggle row ───────────────────────────────────────────────────
            var label = new GUIContent(
                "Is Scene Bound",
                "When ENABLED:\n" +
                "  • UIManager records the scene handle at the moment OpenUI() is called.\n" +
                "  • Whenever the active scene changes, UIManager automatically closes\n" +
                "    all windows whose bound scene handle no longer matches.\n" +
                "  • Safe against rapid repeated scene switches and mid-open transitions:\n" +
                "    the bound scene is frozen at request-time, so even if the scene\n" +
                "    changes before the prefab finishes loading, the window will be\n" +
                "    closed once it opens.\n\n" +
                "When DISABLED (Persistent):\n" +
                "  • The window is never auto-closed by scene changes.\n" +
                "  • Use for global UI that must survive scene transitions\n" +
                "    (main menu, global HUD, loading screen, etc.).\n" +
                "  • You are responsible for explicitly calling CloseUI().");

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(120));
            EditorGUILayout.PropertyField(isSceneBoundProp, GUIContent.none);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(4);

            // ── Contextual help text ─────────────────────────────────────────
            if (config.IsSceneBound)
            {
                EditorGUILayout.HelpBox(
                    "SCENE BOUND — This window will be closed automatically when the active scene changes.\n\n" +
                    "✔ In-game HUD, score screen, level-specific popups\n" +
                    "✔ Any UI that should NOT survive a scene load\n\n" +
                    "Note: you can still override per-call via\n" +
                    "OpenUI(name, ..., isSceneBoundOverride: false).",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "PERSISTENT — This window survives scene transitions.\n\n" +
                    "✔ Main menu, loading screen, global persistent HUD\n" +
                    "✔ Overlay UI managed by the root scene\n\n" +
                    "Remember to call CloseUI() explicitly when no longer needed.",
                    MessageType.None);
            }

            EditorGUILayout.EndVertical();
        }

        // ── Layer section ─────────────────────────────────────────────────────────────────

        private void DrawLayerSection(UIWindowConfiguration config)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

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

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Priority", GUILayout.Width(80));

            int priority = priorityProp.intValue;
            Color oldColor = GUI.color;
            GUI.color = GetPriorityColor(priority);
            EditorGUILayout.IntSlider(priorityProp, -100, 400, GUIContent.none);
            GUI.color = oldColor;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField(GetPriorityHint(priority), EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.Space(3);
            EditorGUILayout.HelpBox(
                "Priority determines render order within the same Layer.\n" +
                "- Higher value = rendered on top (closer to camera)\n" +
                "- Lower value = rendered behind other windows\n" +
                "- Windows with same priority: order depends on open sequence",
                MessageType.None);

            EditorGUILayout.EndVertical();
        }

        // ── Prefab source section ─────────────────────────────────────────────────────────

        private void DrawPrefabSourceSection(UIWindowConfiguration config)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // ── Three-way mode selector ──────────────────────────────────────
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Source Mode", GUILayout.Width(90));

            var currentSource = (UIWindowConfiguration.PrefabSource)sourceProp.enumValueIndex;
            Color oldBg = GUI.backgroundColor;

            GUI.backgroundColor = currentSource == UIWindowConfiguration.PrefabSource.PrefabReference
                ? prefabRefColor : new Color(0.5f, 0.5f, 0.5f, 0.6f);
            if (GUILayout.Button(new GUIContent("Direct Ref", "Bundle prefab directly in build (Resources / built-in scene)."),
                    EditorStyles.miniButtonLeft))
            {
                MigrateSourceData(currentSource, UIWindowConfiguration.PrefabSource.PrefabReference);
                currentSource = UIWindowConfiguration.PrefabSource.PrefabReference;
            }

            GUI.backgroundColor = currentSource == UIWindowConfiguration.PrefabSource.AssetReference
                ? assetRefColor : new Color(0.5f, 0.5f, 0.5f, 0.6f);
            if (GUILayout.Button(new GUIContent("Asset Ref", "Load via CycloneGames.AssetManagement (Addressables / YooAsset)."),
                    EditorStyles.miniButtonMid))
            {
                MigrateSourceData(currentSource, UIWindowConfiguration.PrefabSource.AssetReference);
                currentSource = UIWindowConfiguration.PrefabSource.AssetReference;
            }

            GUI.backgroundColor = currentSource == UIWindowConfiguration.PrefabSource.PathLocation
                ? locationColor : new Color(0.5f, 0.5f, 0.5f, 0.6f);
            if (GUILayout.Button(new GUIContent("Path", "Raw address string for xAsset or any custom loader."),
                    EditorStyles.miniButtonRight))
            {
                MigrateSourceData(currentSource, UIWindowConfiguration.PrefabSource.PathLocation);
                currentSource = UIWindowConfiguration.PrefabSource.PathLocation;
            }

            GUI.backgroundColor = oldBg;
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(_migrationStatusMessage))
            {
                EditorGUILayout.Space(3);
                EditorGUILayout.HelpBox(_migrationStatusMessage, _migrationStatusType);
            }

            EditorGUILayout.Space(5);

            // ── Mode-specific field ──────────────────────────────────────────
            switch (currentSource)
            {
                case UIWindowConfiguration.PrefabSource.PrefabReference:
                    DrawPrefabReferenceField(config);
                    break;
                case UIWindowConfiguration.PrefabSource.AssetReference:
                    DrawAssetRefField(config);
                    break;
                case UIWindowConfiguration.PrefabSource.PathLocation:
                    DrawPathLocationField(config);
                    break;
            }

            EditorGUILayout.EndVertical();

            // ── Help text ────────────────────────────────────────────────────
            string helpText = currentSource switch
            {
                UIWindowConfiguration.PrefabSource.PrefabReference =>
                    "Direct Reference: Prefab is embedded in the build. Best for always-loaded windows (HUD, splash).\n" +
                    "No async loading needed; prefab is immediately available.",
                UIWindowConfiguration.PrefabSource.AssetReference =>
                    "Asset Reference: Prefab key is resolved through CycloneGames.AssetManagement.\n" +
                    "Drag a prefab into the field below; AssetRefPropertyDrawer will auto-record GUID and path.\n" +
                    "Supports Addressables and YooAsset. The AssetRef struct holds zero heap allocation at runtime.",
                UIWindowConfiguration.PrefabSource.PathLocation =>
                    "Path Location: A raw address string passed directly to your custom asset loader.\n" +
                    "Use for xAsset or systems that accept a plain string path/address.",
                _ => string.Empty
            };
            EditorGUILayout.HelpBox(helpText, MessageType.Info);
        }

        // ── PrefabReference field ─────────────────────────────────────────────────────────

        private void DrawPrefabReferenceField(UIWindowConfiguration config)
        {
            DrawSourceTag("DIRECT", prefabRefColor);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Prefab", GUILayout.Width(SourceFieldLabelWidth));
            EditorGUILayout.PropertyField(windowPrefabProp, GUIContent.none);
            EditorGUILayout.EndHorizontal();

            if (config.WindowPrefab != null)
            {
                var uiWindow = config.WindowPrefab.GetComponent<UIWindow>();
                if (uiWindow == null)
                    EditorGUILayout.HelpBox("[ERROR] Prefab must have a UIWindow component!", MessageType.Error);
                else
                    EditorGUILayout.LabelField($"[OK] UIWindow: {config.WindowPrefab.name}", EditorStyles.miniLabel);
            }
        }

        // ── AssetReference field ──────────────────────────────────────────────────────────

        private void DrawAssetRefField(UIWindowConfiguration config)
        {
            DrawSourceTag("ASSET REF", assetRefColor);

            bool isValid = config.PrefabAssetRef.IsValid;

            Rect rowRect = EditorGUILayout.GetControlRect();
            Rect labelRect = new Rect(rowRect.x, rowRect.y, SourceFieldLabelWidth, rowRect.height);
            Rect fieldRect = new Rect(rowRect.x + SourceFieldLabelWidth, rowRect.y, rowRect.width - SourceFieldLabelWidth - 76f, rowRect.height);
            Rect buttonRect = new Rect(rowRect.xMax - 70f, rowRect.y, 70f, rowRect.height);

            EditorGUI.LabelField(labelRect, "Prefab");
            EditorGUI.PropertyField(fieldRect, prefabAssetRefProp, GUIContent.none);

            EditorGUI.BeginDisabledGroup(!isValid);
            if (GUI.Button(buttonRect, "Validate", EditorStyles.miniButton))
                ValidateAssetRef(config);
            EditorGUI.EndDisabledGroup();

            if (isValid)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Path", GUILayout.Width(SourceFieldLabelWidth));
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.TextField(config.PrefabAssetRef.Location);
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.LabelField("[OK] AssetRef resolved and ready for runtime loading. Path is read-only.", EditorStyles.miniLabel);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Drag a prefab into the Prefab field. The AssetRef drawer will automatically record its GUID and path.",
                    MessageType.Warning);
            }
        }

        // ── PathLocation field ────────────────────────────────────────────────────────────

        private void DrawPathLocationField(UIWindowConfiguration config)
        {
            DrawSourceTag("PATH", locationColor);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Path", GUILayout.Width(SourceFieldLabelWidth));
            EditorGUILayout.PropertyField(prefabLocationProp, GUIContent.none);
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrEmpty(config.PrefabLocation))
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField($"[OK] Path: {config.PrefabLocation}", EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Validate", EditorStyles.miniButton, GUILayout.Width(70)))
                    ValidatePathLocation(config);
                EditorGUILayout.EndHorizontal();
            }
            else
            {
                EditorGUILayout.HelpBox("Path is empty. Enter the loader address (e.g. \"Assets/UI/MyWindow.prefab\").", MessageType.Warning);
            }
        }

        // ── Source tag badge ──────────────────────────────────────────────────────────────

        private void DrawSourceTag(string label, Color color)
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 14f);
            rect.width = 72f;
            Color oldBg = GUI.backgroundColor;
            GUI.backgroundColor = color;
            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
            GUI.backgroundColor = oldBg;
            EditorGUI.LabelField(rect, label, _tagLabelStyle);
            EditorGUILayout.Space(2);
        }

        // ── Quick actions ─────────────────────────────────────────────────────────────────

        private void DrawQuickActions(UIWindowConfiguration config)
        {
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Copy Window Name"))
            {
                string windowName = config.name.Replace("UIWindow_", "");
                EditorGUIUtility.systemCopyBuffer = windowName;
                Debug.Log($"[UIWindowConfiguration] Copied window name: {windowName}");
            }

            if (config.WindowPrefab != null && GUILayout.Button("Select Prefab"))
            {
                Selection.activeObject = config.WindowPrefab;
                EditorGUIUtility.PingObject(config.WindowPrefab);
            }

            if (config.Layer != null && GUILayout.Button("Select Layer"))
            {
                Selection.activeObject = config.Layer;
                EditorGUIUtility.PingObject(config.Layer);
            }

            EditorGUILayout.EndHorizontal();
        }

        // ── Helpers ───────────────────────────────────────────────────────────────────────

        private Color GetSourceColor(UIWindowConfiguration.PrefabSource source) => source switch
        {
            UIWindowConfiguration.PrefabSource.PrefabReference => prefabRefColor,
            UIWindowConfiguration.PrefabSource.AssetReference  => assetRefColor,
            UIWindowConfiguration.PrefabSource.PathLocation    => locationColor,
            _                                                   => headerColor
        };

        private void MigrateSourceData(UIWindowConfiguration.PrefabSource currentSource, UIWindowConfiguration.PrefabSource targetSource)
        {
            if (currentSource == targetSource)
            {
                SetMigrationStatus($"Already using {GetSourceDisplayName(targetSource)} mode.", MessageType.None);
                sourceProp.enumValueIndex = (int)targetSource;
                return;
            }

            bool migrated = false;

            switch (currentSource)
            {
                case UIWindowConfiguration.PrefabSource.PrefabReference:
                    migrated = MigrateFromPrefabReference(targetSource);
                    break;
                case UIWindowConfiguration.PrefabSource.AssetReference:
                    migrated = MigrateFromAssetReference(targetSource);
                    break;
                case UIWindowConfiguration.PrefabSource.PathLocation:
                    migrated = MigrateFromPathLocation(targetSource);
                    break;
            }

            sourceProp.enumValueIndex = (int)targetSource;

            if (migrated)
            {
                SetMigrationStatus(
                    $"Migrated data from {GetSourceDisplayName(currentSource)} to {GetSourceDisplayName(targetSource)}.",
                    MessageType.Info);
            }
            else
            {
                SetMigrationStatus(
                    $"Switched to {GetSourceDisplayName(targetSource)}. No compatible source data was available to migrate.",
                    MessageType.Warning);
            }
        }

        private bool MigrateFromPrefabReference(UIWindowConfiguration.PrefabSource targetSource)
        {
            var prefab = windowPrefabProp.objectReferenceValue as UIWindow;
            if (prefab == null)
                return false;

            string assetPath = AssetDatabase.GetAssetPath(prefab);
            if (string.IsNullOrEmpty(assetPath))
                return false;

            switch (targetSource)
            {
                case UIWindowConfiguration.PrefabSource.AssetReference:
                    SetAssetRefFromPath(assetPath);
                    return true;
                case UIWindowConfiguration.PrefabSource.PathLocation:
                    prefabLocationProp.stringValue = assetPath;
                    return true;
            }

            return false;
        }

        private bool MigrateFromAssetReference(UIWindowConfiguration.PrefabSource targetSource)
        {
            var guidProp = prefabAssetRefProp.FindPropertyRelative("m_GUID");
            var locationProp = prefabAssetRefProp.FindPropertyRelative("m_Location");
            string guid = guidProp != null ? guidProp.stringValue : string.Empty;
            string location = locationProp != null ? locationProp.stringValue : string.Empty;

            switch (targetSource)
            {
                case UIWindowConfiguration.PrefabSource.PrefabReference:
                    windowPrefabProp.objectReferenceValue = ResolveWindowPrefab(guid, location);
                    return windowPrefabProp.objectReferenceValue != null;
                case UIWindowConfiguration.PrefabSource.PathLocation:
                    prefabLocationProp.stringValue = location;
                    return !string.IsNullOrEmpty(location);
            }

            return false;
        }

        private bool MigrateFromPathLocation(UIWindowConfiguration.PrefabSource targetSource)
        {
            string path = prefabLocationProp.stringValue;
            if (string.IsNullOrEmpty(path))
                return false;

            switch (targetSource)
            {
                case UIWindowConfiguration.PrefabSource.PrefabReference:
                    windowPrefabProp.objectReferenceValue = ResolveWindowPrefab(string.Empty, path);
                    return windowPrefabProp.objectReferenceValue != null;
                case UIWindowConfiguration.PrefabSource.AssetReference:
                    SetAssetRefFromPath(path);
                    return true;
            }

            return false;
        }

        private void SetMigrationStatus(string message, MessageType type)
        {
            _migrationStatusMessage = message;
            _migrationStatusType = type;
        }

        private static string GetSourceDisplayName(UIWindowConfiguration.PrefabSource source)
        {
            switch (source)
            {
                case UIWindowConfiguration.PrefabSource.PrefabReference: return "Direct Ref";
                case UIWindowConfiguration.PrefabSource.AssetReference: return "Asset Ref";
                case UIWindowConfiguration.PrefabSource.PathLocation: return "Path";
                default: return source.ToString();
            }
        }

        private void SetAssetRefFromPath(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return;

            var guidProp = prefabAssetRefProp.FindPropertyRelative("m_GUID");
            var locationProp = prefabAssetRefProp.FindPropertyRelative("m_Location");
            if (guidProp == null || locationProp == null)
                return;

            guidProp.stringValue = AssetDatabase.AssetPathToGUID(assetPath);
            locationProp.stringValue = assetPath;
        }

        private UIWindow ResolveWindowPrefab(string guid, string fallbackPath)
        {
            string assetPath = !string.IsNullOrEmpty(guid)
                ? AssetDatabase.GUIDToAssetPath(guid)
                : fallbackPath;

            if (string.IsNullOrEmpty(assetPath))
                assetPath = fallbackPath;

            if (string.IsNullOrEmpty(assetPath))
                return null;

            var prefabGo = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            return prefabGo != null ? prefabGo.GetComponent<UIWindow>() : null;
        }

        private Color GetPriorityColor(int priority)
        {
            if (priority < 0)   return new Color(0.5f, 0.5f, 0.8f);
            if (priority < 100) return Color.white;
            if (priority < 200) return new Color(0.9f, 0.8f, 0.3f);
            return new Color(0.9f, 0.4f, 0.3f);
        }

        private string GetPriorityHint(int priority)
        {
            if (priority < 0)   return "<< Below Default (Background)";
            if (priority == 0)  return "-- Default Priority";
            if (priority < 100) return ">> Above Default";
            if (priority < 200) return ">>> High Priority (Modal)";
            if (priority < 300) return ">>>> Very High (Overlay)";
            return ">>>>> Maximum Priority (System)";
        }

        // ── Duplicate detection ────────────────────────────────────────────────────────────

        private void CheckForDuplicates()
        {
            if (!hasCheckedForDuplicates || Event.current.type == EventType.Layout)
            {
                allConfigGuids          = AssetDatabase.FindAssets("t:UIWindowConfiguration");
                hasCheckedForDuplicates = true;
            }
        }

        // ── Foldout header ─────────────────────────────────────────────────────────────────

        private bool DrawFoldoutHeader(string title, bool foldout, Color color)
        {
            EditorGUILayout.Space(2);
            Rect rect = EditorGUILayout.GetControlRect(false, 22);

            Color bgColor = foldout
                ? color
                : new Color(color.r * 0.7f, color.g * 0.7f, color.b * 0.7f);
            EditorGUI.DrawRect(rect, bgColor);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y,       rect.width, 1f), Color.black * 0.2f);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1f), Color.black * 0.2f);

            Rect labelRect = new Rect(rect.x + 20, rect.y, rect.width - 20, rect.height);
            EditorGUI.LabelField(labelRect, title, _foldoutLabelStyle);

            Rect arrowRect = new Rect(rect.x + 5, rect.y, 15, rect.height);
            EditorGUI.LabelField(arrowRect, foldout ? "v" : ">", _foldoutLabelStyle);

            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                foldout = !foldout;
                Event.current.Use();
            }
            return foldout;
        }

        // ── Validation ────────────────────────────────────────────────────────────────────

        private void ValidateAssetRef(UIWindowConfiguration config)
        {
            string location = config.PrefabAssetRef.Location;
            if (string.IsNullOrEmpty(location))
            {
                EditorUtility.DisplayDialog("UIWindowConfiguration", "AssetRef Location is empty.", "OK");
                return;
            }

            var pkg = AssetManagementLocator.DefaultPackage;
            if (pkg == null)
            {
                EditorUtility.DisplayDialog("UIWindowConfiguration",
                    "DefaultPackage is null.\n\nInitialize AssetManagement and assign DefaultPackage first.", "OK");
                return;
            }

            var handle = pkg.LoadAssetAsync<GameObject>(location);
            EditorApplication.delayCall += () =>
            {
                if (!handle.IsDone)
                    EditorApplication.delayCall += () => FinalizeValidation(handle, location);
                else
                    FinalizeValidation(handle, location);
            };
        }

        private void ValidatePathLocation(UIWindowConfiguration config)
        {
            string path = config.PrefabLocation;
            if (string.IsNullOrEmpty(path))
            {
                EditorUtility.DisplayDialog("UIWindowConfiguration", "Path is empty.", "OK");
                return;
            }

            // Path-only validation: check if the asset exists in the AssetDatabase
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (asset != null)
            {
                EditorUtility.DisplayDialog("Validation Result",
                    $"[OK] Asset found at path:\n{path}\n\nPrefab: {asset.name}", "OK");
            }
            else
            {
                EditorUtility.DisplayDialog("Validation Result",
                    $"[FAIL] No asset found at path:\n{path}\n\n" +
                    "Note: path-based loaders (xAsset, etc.) may use virtual paths not tracked by AssetDatabase.", "OK");
            }
        }

        private void FinalizeValidation(IAssetHandle<GameObject> handle, string location)
        {
            if (handle == null)
            {
                EditorUtility.DisplayDialog("UIWindowConfiguration", "Handle null during validation.", "OK");
                return;
            }

            bool success = string.IsNullOrEmpty(handle.Error) && handle.Asset != null;
            string icon  = success ? "[OK]" : "[FAIL]";
            string msg   = success
                ? $"{icon} Location Valid\n\nPath: {location}\nPrefab: {handle.Asset?.name}"
                : $"{icon} Location Invalid\n\nPath: {location}\nError: {handle.Error}";

            handle.Dispose();
            EditorUtility.DisplayDialog("Validation Result", msg, "OK");
        }
    }
}
