using CycloneGames.GameplayFramework.Runtime;
using CycloneGames.Logging;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime.Editor
{
    [CustomEditor(typeof(WorldSettings), true)]
    public class WorldSettingsEditor : UnityEditor.Editor
    {
        private static readonly LogChannel Log = GameplayFrameworkEditorLog.Channel;

        private enum ReferenceValidationState : byte
        {
            Missing = 0,
            Configured = 1,
            Unresolved = 2,
            Invalid = 3,
        }

        private static readonly Color editableHeaderColor = new Color(0.50f, 0.58f, 0.38f);
        private static readonly Color validationHeaderColor = new Color(0.30f, 0.50f, 0.70f);
        private static readonly Color ValidColor = new Color(0.7f, 1f, 0.7f);
        private static readonly Color WarningColor = new Color(1f, 0.9f, 0.5f);
        private static readonly Color ErrorColor = new Color(1f, 0.6f, 0.6f);
        private static readonly string[] ReferenceModeLabels = { "Direct Ref", "Asset Ref", "Path" };

        private bool showValidation = true;
        private bool showConfiguration = true;

        private SerializedProperty gameModeClassProp;
        private SerializedProperty gameModeSourceProp;
        private SerializedProperty gameModeAssetLocationProp;

        private SerializedProperty playerControllerClassProp;
        private SerializedProperty playerControllerSourceProp;
        private SerializedProperty playerControllerAssetLocationProp;

        private SerializedProperty pawnClassProp;
        private SerializedProperty pawnSourceProp;
        private SerializedProperty pawnAssetLocationProp;

        private SerializedProperty playerStateClassProp;
        private SerializedProperty playerStateSourceProp;
        private SerializedProperty playerStateAssetLocationProp;

        private SerializedProperty cameraManagerClassProp;
        private SerializedProperty cameraManagerSourceProp;
        private SerializedProperty cameraManagerAssetLocationProp;

        private SerializedProperty spectatorPawnClassProp;
        private SerializedProperty spectatorPawnSourceProp;
        private SerializedProperty spectatorPawnAssetLocationProp;

        private void OnEnable()
        {
            gameModeClassProp = serializedObject.FindProperty("gameModeClass");
            gameModeSourceProp = serializedObject.FindProperty("gameModeSource");
            gameModeAssetLocationProp = serializedObject.FindProperty("gameModeAssetLocation");

            playerControllerClassProp = serializedObject.FindProperty("playerControllerClass");
            playerControllerSourceProp = serializedObject.FindProperty("playerControllerSource");
            playerControllerAssetLocationProp = serializedObject.FindProperty("playerControllerAssetLocation");

            pawnClassProp = serializedObject.FindProperty("pawnClass");
            pawnSourceProp = serializedObject.FindProperty("pawnSource");
            pawnAssetLocationProp = serializedObject.FindProperty("pawnAssetLocation");

            playerStateClassProp = serializedObject.FindProperty("playerStateClass");
            playerStateSourceProp = serializedObject.FindProperty("playerStateSource");
            playerStateAssetLocationProp = serializedObject.FindProperty("playerStateAssetLocation");

            cameraManagerClassProp = serializedObject.FindProperty("cameraManagerClass");
            cameraManagerSourceProp = serializedObject.FindProperty("cameraManagerSource");
            cameraManagerAssetLocationProp = serializedObject.FindProperty("cameraManagerAssetLocation");

            spectatorPawnClassProp = serializedObject.FindProperty("spectatorPawnClass");
            spectatorPawnSourceProp = serializedObject.FindProperty("spectatorPawnSource");
            spectatorPawnAssetLocationProp = serializedObject.FindProperty("spectatorPawnAssetLocation");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            var ws = (WorldSettings)target;
            showValidation = InspectorUiUtility.DrawFoldoutHeader("Validation Overview", showValidation, validationHeaderColor);
            if (showValidation)
            {
                EditorGUILayout.BeginVertical(GUI.skin.box);
                InspectorUiUtility.DrawSectionHeader("Configuration Status", "WorldSettings is authoring-only. GameInstance resolves external locations through its explicitly composed IWorldSettingsReferenceResolver and owns the resulting lease.", new Color(0.42f, 0.78f, 1f, 1f));
                DrawRequirementLegend();
                DrawValidationStatus("GameMode", GetValidationState(gameModeSourceProp, gameModeClassProp, gameModeAssetLocationProp), true, GetSource(gameModeSourceProp));
                DrawValidationStatus("PlayerController", GetValidationState(playerControllerSourceProp, playerControllerClassProp, playerControllerAssetLocationProp), true, GetSource(playerControllerSourceProp));
                DrawValidationStatus("Pawn", GetValidationState(pawnSourceProp, pawnClassProp, pawnAssetLocationProp), true, GetSource(pawnSourceProp));
                DrawValidationStatus("PlayerState", GetValidationState(playerStateSourceProp, playerStateClassProp, playerStateAssetLocationProp), true, GetSource(playerStateSourceProp));
                DrawValidationStatus("CameraManager", GetValidationState(cameraManagerSourceProp, cameraManagerClassProp, cameraManagerAssetLocationProp), false, GetSource(cameraManagerSourceProp));
                DrawValidationStatus("SpectatorPawn", GetValidationState(spectatorPawnSourceProp, spectatorPawnClassProp, spectatorPawnAssetLocationProp), false, GetSource(spectatorPawnSourceProp));
                EditorGUILayout.Space(4f);
                EditorGUILayout.HelpBox(
                    "External modes require an IWorldSettingsReferenceResolver passed to the GameInstance constructor. Resolver availability is a runtime composition decision and is not stored globally.",
                    MessageType.Info);
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space(6f);

            showConfiguration = InspectorUiUtility.DrawFoldoutHeader("Reference Configuration", showConfiguration, editableHeaderColor);
            if (showConfiguration)
            {
                EditorGUILayout.BeginVertical(GUI.skin.box);
                InspectorUiUtility.DrawSectionHeader("Reference Strategy", "Choose Direct Reference for project-owned prefab assets, Asset Reference for CycloneGames.AssetManagement-backed loading, or Path for a custom address-based resource manager.", new Color(1f, 0.76f, 0.38f, 1f));
                DrawReferenceEntry<GameMode>("GameMode", "Class Prefab", gameModeSourceProp, gameModeClassProp, gameModeAssetLocationProp, true);
                DrawReferenceEntry<PlayerController>("PlayerController", "Default Class Prefab", playerControllerSourceProp, playerControllerClassProp, playerControllerAssetLocationProp, true);
                DrawReferenceEntry<Pawn>("Pawn", "Default Class Prefab", pawnSourceProp, pawnClassProp, pawnAssetLocationProp, true);
                DrawReferenceEntry<PlayerState>("PlayerState", "Default Class Prefab", playerStateSourceProp, playerStateClassProp, playerStateAssetLocationProp, true);
                DrawReferenceEntry<CameraManager>("CameraManager", "Default Class Prefab", cameraManagerSourceProp, cameraManagerClassProp, cameraManagerAssetLocationProp, false);
                DrawReferenceEntry<SpectatorPawn>("SpectatorPawn", "Default Class Prefab", spectatorPawnSourceProp, spectatorPawnClassProp, spectatorPawnAssetLocationProp, false);
                EditorGUILayout.EndVertical();
            }

            if (GUILayout.Button("Validate Configuration"))
            {
                serializedObject.ApplyModifiedProperties();
                bool valid = ws.Validate();
                if (valid)
                {
                    Log.Info(
                        ws,
                        static (settings, builder) =>
                        {
                            builder.Append("WorldSettings '");
                            builder.Append(settings.name);
                            builder.Append("' required references are valid; CameraManager and SpectatorPawn are optional.");
                        });
                }

                serializedObject.Update();
            }

            EditorGUILayout.Space(8);
            if (serializedObject.ApplyModifiedProperties())
            {
                Repaint();
            }
        }

        private static void DrawValidationStatus(
            string label,
            ReferenceValidationState state,
            bool required,
            WorldSettingsReferenceSource source)
        {
            string modeLabel = GetModeLabel(source);

            if (state == ReferenceValidationState.Configured)
            {
                GUI.color = ValidColor;
                EditorGUILayout.LabelField($"  \u2713 {label} [{modeLabel}]", EditorStyles.miniLabel);
            }
            else if (state == ReferenceValidationState.Unresolved)
            {
                GUI.color = ErrorColor;
                EditorGUILayout.LabelField($"  ! {label} [{modeLabel}] (Unresolved)", EditorStyles.miniBoldLabel);
            }
            else if (state == ReferenceValidationState.Invalid)
            {
                GUI.color = ErrorColor;
                EditorGUILayout.LabelField($"  ! {label} [{modeLabel}] (Prefab Required)", EditorStyles.miniBoldLabel);
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
            string directFieldLabel,
            SerializedProperty sourceProp,
            SerializedProperty directReferenceProp,
            SerializedProperty assetLocationProp,
            bool required) where T : Component
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            if (!required)
            {
                EditorGUILayout.HelpBox($"{label} is optional. Leaving it empty will not block core gameplay framework initialization.", MessageType.Info);

                if (label == "CameraManager")
                {
                    EditorGUILayout.HelpBox("CameraManager is optional by design (similar to Unreal-style setups). If left empty, gameplay logic can still run but this camera module will not drive view blending/poses.", MessageType.None);
                }
            }

            WorldSettingsReferenceSource source = GetSource(sourceProp);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Mode", GUILayout.Width(80f));
            WorldSettingsReferenceSource nextSource = DrawModeToolbar(source);
            if (nextSource != source)
            {
                sourceProp.enumValueIndex = (int)nextSource;
                source = nextSource;
            }
            EditorGUILayout.EndHorizontal();

            if (source == WorldSettingsReferenceSource.DirectReference)
            {
                DrawDirectPrefabField<T>(directReferenceProp, directFieldLabel);
            }
            else if (source == WorldSettingsReferenceSource.AssetReference)
            {
                DrawAssetReferenceField<T>(assetLocationProp);
            }
            else
            {
                DrawPathReferenceField(assetLocationProp);
            }

            ReferenceValidationState state = GetValidationState(sourceProp, directReferenceProp, assetLocationProp);
            if (state == ReferenceValidationState.Unresolved)
            {
                EditorGUILayout.HelpBox(
                    $"{label} has a serialized reference, but Unity cannot resolve its component. Fix script compilation errors, wait for domain reload, and reimport the prefab.",
                    MessageType.Error);
            }
            else if (state == ReferenceValidationState.Invalid)
            {
                EditorGUILayout.HelpBox(
                    $"{label} must reference a component on a prefab asset. Scene objects are not valid WorldSettings authoring references.",
                    MessageType.Error);
            }
            else if (required && state == ReferenceValidationState.Missing)
            {
                EditorGUILayout.HelpBox($"{label} is required.", MessageType.Warning);
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(4f);
        }

        private static void DrawRequirementLegend()
        {
            EditorGUILayout.HelpBox("Legend: Required entries must be configured to boot the gameplay loop. Optional entries may be omitted depending on your project architecture.", MessageType.None);
        }

        private void DrawAssetReferenceField<T>(SerializedProperty assetLocationProp) where T : Object
        {
            T currentAsset = LoadAssetAtPath<T>(assetLocationProp.stringValue);
            T selectedAsset = (T)EditorGUILayout.ObjectField("Asset", currentAsset, typeof(T), false);
            if (selectedAsset != currentAsset)
            {
                AssignAssetReference(selectedAsset, assetLocationProp);
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

            EditorGUILayout.HelpBox("Resolve this location with the IWorldSettingsReferenceResolver passed to GameInstance.", MessageType.Info);
        }

        private static void DrawPathReferenceField(SerializedProperty assetLocationProp)
        {
            EditorGUILayout.PropertyField(assetLocationProp, new GUIContent("Path"));
            EditorGUILayout.HelpBox("Use Path mode for a custom address-based loader. Pass an IWorldSettingsReferenceResolver that supports PathLocation to GameInstance.", MessageType.Info);
        }

        private static void DrawDirectPrefabField<T>(
            SerializedProperty directReferenceProp,
            string fieldLabel) where T : Component
        {
            ReferenceValidationState state = GetDirectReferenceState(directReferenceProp);
            if (state == ReferenceValidationState.Unresolved)
            {
                EditorGUILayout.PropertyField(directReferenceProp, new GUIContent(fieldLabel));
                return;
            }

            T currentComponent = directReferenceProp.objectReferenceValue as T;
            GameObject currentPrefab = currentComponent != null ? currentComponent.gameObject : null;

            EditorGUI.BeginChangeCheck();
            GameObject selectedPrefab = (GameObject)EditorGUILayout.ObjectField(
                fieldLabel,
                currentPrefab,
                typeof(GameObject),
                allowSceneObjects: false);
            if (!EditorGUI.EndChangeCheck())
            {
                return;
            }

            if (selectedPrefab == null)
            {
                directReferenceProp.objectReferenceValue = null;
                return;
            }

            if (!PrefabUtility.IsPartOfPrefabAsset(selectedPrefab))
            {
                Log.Warning(
                    selectedPrefab,
                    static (prefab, builder) =>
                    {
                        builder.Append("WorldSettings direct references require prefab assets; '");
                        builder.Append(prefab.name);
                        builder.Append("' was not assigned.");
                    });
                return;
            }

            T[] components = selectedPrefab.GetComponents<T>();
            if (components.Length != 1)
            {
                Log.Warning(
                    (ComponentType: typeof(T), Prefab: selectedPrefab, Count: components.Length),
                    static (state, builder) =>
                    {
                        builder.Append("WorldSettings expected exactly one ");
                        builder.Append(state.ComponentType.Name);
                        builder.Append(" component on prefab '");
                        builder.Append(state.Prefab.name);
                        builder.Append("', but found ");
                        builder.Append(state.Count);
                        builder.Append(". The existing reference was preserved.");
                    });
                return;
            }

            directReferenceProp.objectReferenceValue = components[0];
        }

        private static ReferenceValidationState GetValidationState(
            SerializedProperty sourceProp,
            SerializedProperty directReferenceProp,
            SerializedProperty assetLocationProp)
        {
            return GetSource(sourceProp) == WorldSettingsReferenceSource.DirectReference
                ? GetDirectReferenceState(directReferenceProp)
                : string.IsNullOrWhiteSpace(assetLocationProp.stringValue)
                    ? ReferenceValidationState.Missing
                    : ReferenceValidationState.Configured;
        }

        private static ReferenceValidationState GetDirectReferenceState(SerializedProperty directReferenceProp)
        {
            Object directReference = directReferenceProp.objectReferenceValue;
            if (directReference != null)
            {
                return PrefabUtility.IsPartOfPrefabAsset(directReference)
                    ? ReferenceValidationState.Configured
                    : ReferenceValidationState.Invalid;
            }

            return directReferenceProp.objectReferenceInstanceIDValue != 0
                ? ReferenceValidationState.Unresolved
                : ReferenceValidationState.Missing;
        }

        private static WorldSettingsReferenceSource GetSource(SerializedProperty sourceProp)
        {
            return (WorldSettingsReferenceSource)sourceProp.intValue;
        }

        private static T LoadAssetAtPath<T>(string assetPath) where T : Object
        {
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<T>(assetPath);
        }

        private static void AssignAssetReference(Object asset, SerializedProperty assetLocationProp)
        {
            if (asset == null)
            {
                assetLocationProp.stringValue = string.Empty;
                return;
            }

            string assetPath = AssetDatabase.GetAssetPath(asset);
            assetLocationProp.stringValue = assetPath;
        }

        private static WorldSettingsReferenceSource DrawModeToolbar(WorldSettingsReferenceSource source)
        {
            int selectedIndex = source == WorldSettingsReferenceSource.PathLocation ? 2 : (int)source;
            int nextIndex = GUILayout.Toolbar(selectedIndex, ReferenceModeLabels);

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
