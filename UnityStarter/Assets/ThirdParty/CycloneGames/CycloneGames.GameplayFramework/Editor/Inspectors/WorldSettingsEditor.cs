using UnityEditor;
using UnityEngine;
using CycloneGames.GameplayFramework.Runtime;

namespace CycloneGames.GameplayFramework.Runtime.Editor
{
    [CustomEditor(typeof(WorldSettings), true)]
    public class WorldSettingsEditor : UnityEditor.Editor
    {
        private static readonly Color editableHeaderColor = new Color(0.50f, 0.58f, 0.38f);
        private static readonly Color validationHeaderColor = new Color(0.30f, 0.50f, 0.70f);
        private static readonly Color ValidColor = new Color(0.7f, 1f, 0.7f);
        private static readonly Color WarningColor = new Color(1f, 0.9f, 0.5f);
        private static readonly Color ErrorColor = new Color(1f, 0.6f, 0.6f);

        private bool showValidation = true;
        private bool showConfiguration = true;

        private SerializedProperty gameModeClassProp;
        private SerializedProperty gameModeSourceProp;
        private SerializedProperty gameModeAssetLocationProp;
        private SerializedProperty gameModeAssetGuidProp;

        private SerializedProperty playerControllerClassProp;
        private SerializedProperty playerControllerSourceProp;
        private SerializedProperty playerControllerAssetLocationProp;
        private SerializedProperty playerControllerAssetGuidProp;

        private SerializedProperty pawnClassProp;
        private SerializedProperty pawnSourceProp;
        private SerializedProperty pawnAssetLocationProp;
        private SerializedProperty pawnAssetGuidProp;

        private SerializedProperty playerStateClassProp;
        private SerializedProperty playerStateSourceProp;
        private SerializedProperty playerStateAssetLocationProp;
        private SerializedProperty playerStateAssetGuidProp;

        private SerializedProperty cameraManagerClassProp;
        private SerializedProperty cameraManagerSourceProp;
        private SerializedProperty cameraManagerAssetLocationProp;
        private SerializedProperty cameraManagerAssetGuidProp;

        private SerializedProperty spectatorPawnClassProp;
        private SerializedProperty spectatorPawnSourceProp;
        private SerializedProperty spectatorPawnAssetLocationProp;
        private SerializedProperty spectatorPawnAssetGuidProp;

        private void OnEnable()
        {
            gameModeClassProp = serializedObject.FindProperty("gameModeClass");
            gameModeSourceProp = serializedObject.FindProperty("gameModeSource");
            gameModeAssetLocationProp = serializedObject.FindProperty("gameModeAssetLocation");
            gameModeAssetGuidProp = serializedObject.FindProperty("gameModeAssetGuid");

            playerControllerClassProp = serializedObject.FindProperty("playerControllerClass");
            playerControllerSourceProp = serializedObject.FindProperty("playerControllerSource");
            playerControllerAssetLocationProp = serializedObject.FindProperty("playerControllerAssetLocation");
            playerControllerAssetGuidProp = serializedObject.FindProperty("playerControllerAssetGuid");

            pawnClassProp = serializedObject.FindProperty("pawnClass");
            pawnSourceProp = serializedObject.FindProperty("pawnSource");
            pawnAssetLocationProp = serializedObject.FindProperty("pawnAssetLocation");
            pawnAssetGuidProp = serializedObject.FindProperty("pawnAssetGuid");

            playerStateClassProp = serializedObject.FindProperty("playerStateClass");
            playerStateSourceProp = serializedObject.FindProperty("playerStateSource");
            playerStateAssetLocationProp = serializedObject.FindProperty("playerStateAssetLocation");
            playerStateAssetGuidProp = serializedObject.FindProperty("playerStateAssetGuid");

            cameraManagerClassProp = serializedObject.FindProperty("cameraManagerClass");
            cameraManagerSourceProp = serializedObject.FindProperty("cameraManagerSource");
            cameraManagerAssetLocationProp = serializedObject.FindProperty("cameraManagerAssetLocation");
            cameraManagerAssetGuidProp = serializedObject.FindProperty("cameraManagerAssetGuid");

            spectatorPawnClassProp = serializedObject.FindProperty("spectatorPawnClass");
            spectatorPawnSourceProp = serializedObject.FindProperty("spectatorPawnSource");
            spectatorPawnAssetLocationProp = serializedObject.FindProperty("spectatorPawnAssetLocation");
            spectatorPawnAssetGuidProp = serializedObject.FindProperty("spectatorPawnAssetGuid");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var ws = (WorldSettings)target;
            bool assetManagementAvailable = WorldSettingsAssetManagementBridge.IsAvailable;

            showValidation = InspectorUiUtility.DrawFoldoutHeader("Validation Overview", showValidation, validationHeaderColor);
            if (showValidation)
            {
                EditorGUILayout.BeginVertical(GUI.skin.box);
                InspectorUiUtility.DrawSectionHeader("Configuration Status", "Direct references remain supported. Asset reference mode stores location strings and resolves them at runtime through CycloneGames.AssetManagement when available.", new Color(0.42f, 0.78f, 1f, 1f));
                DrawValidationStatus("GameMode", ws.HasConfiguredGameMode, true, ws.GameModeSource);
                DrawValidationStatus("PlayerController", ws.HasConfiguredPlayerController, true, ws.PlayerControllerSource);
                DrawValidationStatus("Pawn", ws.HasConfiguredPawn, true, ws.PawnSource);
                DrawValidationStatus("PlayerState", ws.HasConfiguredPlayerState, false, ws.PlayerStateSource);
                DrawValidationStatus("CameraManager", ws.HasConfiguredCameraManager, false, ws.CameraManagerSource);
                DrawValidationStatus("SpectatorPawn", ws.HasConfiguredSpectatorPawn, false, ws.SpectatorPawnSource);
                EditorGUILayout.Space(4f);
                EditorGUILayout.HelpBox(
                    assetManagementAvailable
                        ? "CycloneGames.AssetManagement was detected. Asset Reference mode will resolve via AssetManagementLocator.DefaultPackage during startup. Path mode can be resolved by a custom IWorldSettingsReferenceResolver."
                        : "CycloneGames.AssetManagement was not detected. Asset Reference mode remains editable but unresolved until the package is installed. Path mode can still work through a custom IWorldSettingsReferenceResolver.",
                    assetManagementAvailable ? MessageType.Info : MessageType.Warning);
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space(6f);

            showConfiguration = InspectorUiUtility.DrawFoldoutHeader("Reference Configuration", showConfiguration, editableHeaderColor);
            if (showConfiguration)
            {
                EditorGUILayout.BeginVertical(GUI.skin.box);
                InspectorUiUtility.DrawSectionHeader("Reference Strategy", "Choose Direct Reference for legacy drag-and-drop, Asset Reference for CycloneGames.AssetManagement-backed loading, or Path for any custom address-based resource manager.", new Color(1f, 0.76f, 0.38f, 1f));
                DrawReferenceEntry<GameMode>("GameMode", gameModeSourceProp, gameModeClassProp, gameModeAssetLocationProp, gameModeAssetGuidProp, true, assetManagementAvailable);
                DrawReferenceEntry<PlayerController>("PlayerController", playerControllerSourceProp, playerControllerClassProp, playerControllerAssetLocationProp, playerControllerAssetGuidProp, true, assetManagementAvailable);
                DrawReferenceEntry<Pawn>("Pawn", pawnSourceProp, pawnClassProp, pawnAssetLocationProp, pawnAssetGuidProp, true, assetManagementAvailable);
                DrawReferenceEntry<PlayerState>("PlayerState", playerStateSourceProp, playerStateClassProp, playerStateAssetLocationProp, playerStateAssetGuidProp, false, assetManagementAvailable);
                DrawReferenceEntry<CameraManager>("CameraManager", cameraManagerSourceProp, cameraManagerClassProp, cameraManagerAssetLocationProp, cameraManagerAssetGuidProp, false, assetManagementAvailable);
                DrawReferenceEntry<SpectatorPawn>("SpectatorPawn", spectatorPawnSourceProp, spectatorPawnClassProp, spectatorPawnAssetLocationProp, spectatorPawnAssetGuidProp, false, assetManagementAvailable);
                EditorGUILayout.EndVertical();
            }

            if (GUILayout.Button("Validate Configuration"))
            {
                bool valid = ws.Validate();
                if (valid)
                {
                    Debug.Log($"[WorldSettings] '{ws.name}': All required references are assigned.");
                }
            }

            EditorGUILayout.Space(8);
            serializedObject.ApplyModifiedProperties();
        }

        private static void DrawValidationStatus(string label, bool configured, bool required, WorldSettingsReferenceSource source)
        {
            string modeLabel = GetModeLabel(source);

            if (configured)
            {
                GUI.color = ValidColor;
                EditorGUILayout.LabelField($"  \u2713 {label} [{modeLabel}]", EditorStyles.miniLabel);
            }
            else if (required)
            {
                GUI.color = ErrorColor;
                EditorGUILayout.LabelField($"  \u2717 {label} [{modeLabel}] (Required)", EditorStyles.miniBoldLabel);
            }
            else
            {
                GUI.color = WarningColor;
                EditorGUILayout.LabelField($"  \u25CB {label} [{modeLabel}] (Optional)", EditorStyles.miniLabel);
            }

            GUI.color = Color.white;
        }

        private void DrawReferenceEntry<T>(
            string label,
            SerializedProperty sourceProp,
            SerializedProperty directReferenceProp,
            SerializedProperty assetLocationProp,
            SerializedProperty assetGuidProp,
            bool required,
            bool assetManagementAvailable) where T : Object
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);

            WorldSettingsReferenceSource source = (WorldSettingsReferenceSource)sourceProp.enumValueIndex;
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Mode", GUILayout.Width(80f));
            WorldSettingsReferenceSource nextSource = DrawModeToolbar(source);
            if (nextSource != source)
            {
                sourceProp.enumValueIndex = (int)nextSource;
                source = nextSource;
                if (source == WorldSettingsReferenceSource.DirectReference)
                {
                    assetLocationProp.stringValue = string.Empty;
                    assetGuidProp.stringValue = string.Empty;
                }
                else
                {
                    directReferenceProp.objectReferenceValue = null;
                }
            }
            EditorGUILayout.EndHorizontal();

            if (source == WorldSettingsReferenceSource.DirectReference)
            {
                EditorGUILayout.PropertyField(directReferenceProp, new GUIContent("Prefab"));
            }
            else if (source == WorldSettingsReferenceSource.AssetReference)
            {
                DrawAssetReferenceField<T>(assetLocationProp, assetGuidProp, assetManagementAvailable);
            }
            else
            {
                DrawPathReferenceField(assetLocationProp);
            }

            if (required && !IsConfigured(source, directReferenceProp, assetLocationProp))
            {
                EditorGUILayout.HelpBox($"{label} is required.", MessageType.Warning);
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(4f);
        }

        private void DrawAssetReferenceField<T>(SerializedProperty assetLocationProp, SerializedProperty assetGuidProp, bool assetManagementAvailable) where T : Object
        {
            T currentAsset = LoadAssetAtPath<T>(assetLocationProp.stringValue);
            T selectedAsset = (T)EditorGUILayout.ObjectField("Asset", currentAsset, typeof(T), false);
            if (selectedAsset != currentAsset)
            {
                AssignAssetReference(selectedAsset, assetLocationProp, assetGuidProp);
                currentAsset = selectedAsset;
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("Location", assetLocationProp.stringValue);
            }

            if (!string.IsNullOrEmpty(assetLocationProp.stringValue) && currentAsset == null)
            {
                EditorGUILayout.HelpBox("The stored asset location is set, but the asset could not be found in the AssetDatabase. Runtime loading may still work if the provider resolves this address virtually.", MessageType.Warning);
            }

            if (!assetManagementAvailable)
            {
                EditorGUILayout.HelpBox("Asset Reference mode is configured, but CycloneGames.AssetManagement is not currently available in this project.", MessageType.Warning);
            }
        }

        private static void DrawPathReferenceField(SerializedProperty assetLocationProp)
        {
            EditorGUILayout.PropertyField(assetLocationProp, new GUIContent("Path"));
            EditorGUILayout.HelpBox("Use Path mode for xAsset or any custom address-based loader. Register an IWorldSettingsReferenceResolver for PathLocation to resolve these entries at runtime.", MessageType.Info);
        }

        private static bool IsConfigured(WorldSettingsReferenceSource source, SerializedProperty directReferenceProp, SerializedProperty assetLocationProp)
        {
            return source == WorldSettingsReferenceSource.DirectReference
                ? directReferenceProp.objectReferenceValue != null
                : !string.IsNullOrWhiteSpace(assetLocationProp.stringValue);
        }

        private static T LoadAssetAtPath<T>(string assetPath) where T : Object
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<T>(assetPath);
        }

        private static void AssignAssetReference(Object asset, SerializedProperty assetLocationProp, SerializedProperty assetGuidProp)
        {
            if (asset == null)
            {
                assetLocationProp.stringValue = string.Empty;
                assetGuidProp.stringValue = string.Empty;
                return;
            }

            string assetPath = AssetDatabase.GetAssetPath(asset);
            assetLocationProp.stringValue = assetPath;
            assetGuidProp.stringValue = AssetDatabase.AssetPathToGUID(assetPath);
        }

        private static WorldSettingsReferenceSource DrawModeToolbar(WorldSettingsReferenceSource source)
        {
            int selectedIndex = source == WorldSettingsReferenceSource.PathLocation ? 2 : (int)source;
            int nextIndex = GUILayout.Toolbar(selectedIndex, new[] { "Direct Ref", "Asset Ref", "Path" });

            switch (nextIndex)
            {
                case 1:
                    return WorldSettingsReferenceSource.AssetReference;
                case 2:
                    return WorldSettingsReferenceSource.PathLocation;
                default:
                    return WorldSettingsReferenceSource.DirectReference;
            }
        }

        private static string GetModeLabel(WorldSettingsReferenceSource source)
        {
            switch (source)
            {
                case WorldSettingsReferenceSource.AssetReference:
                    return "Asset";
                case WorldSettingsReferenceSource.PathLocation:
                    return "Path";
                default:
                    return "Direct";
            }
        }
    }
}
