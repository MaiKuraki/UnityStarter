using System;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Networking.Editor
{
    [CustomEditor(typeof(GASNetworkContentCatalogAsset), true)]
    [CanEditMultipleObjects]
    public sealed class GASNetworkContentCatalogAssetEditor : UnityEditor.Editor
    {
        private static readonly GUIContent EntriesLabel = new GUIContent("Entries");
        private static readonly GUIContent ValidateLabel = new GUIContent(
            "Validate Catalog",
            "Builds each selected immutable catalog in memory. No asset or revision is modified.");

        private SerializedProperty script;
        private SerializedProperty abilities;
        private SerializedProperty effects;
        private SerializedProperty attributes;
        private SerializedProperty setByCallerNames;
        private SerializedProperty targetSurfaces;

        private bool showAbilities = true;
        private bool showEffects = true;
        private bool showAttributes;
        private bool showSetByCallerNames;
        private bool showTargetSurfaces;
        private int lastAbilityCount = -1;
        private int lastEffectCount = -1;
        private int lastAttributeCount = -1;
        private int lastSetByCallerCount = -1;
        private int lastTargetSurfaceCount = -1;
        private string summary;
        private string validationMessage;
        private MessageType validationMessageType;

        private void OnEnable()
        {
            script = serializedObject.FindProperty("m_Script");
            abilities = serializedObject.FindProperty("abilities");
            effects = serializedObject.FindProperty("effects");
            attributes = serializedObject.FindProperty("attributes");
            setByCallerNames = serializedObject.FindProperty("setByCallerNames");
            targetSurfaces = serializedObject.FindProperty("targetSurfaces");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawScriptReference();
            DrawSummary();
            EditorGUILayout.Space(4f);

            DrawGroup("Abilities", abilities, ref showAbilities);
            DrawGroup("Effects", effects, ref showEffects);
            DrawGroup("Attributes", attributes, ref showAttributes);
            DrawGroup("SetByCaller Names", setByCallerNames, ref showSetByCallerNames);
            DrawGroup("Target Surfaces", targetSurfaces, ref showTargetSurfaces);

            bool changed = serializedObject.ApplyModifiedProperties();
            if (changed)
            {
                validationMessage = null;
                InvalidateSummary();
            }

            EditorGUILayout.Space(6f);
            using (new EditorGUI.DisabledScope(HasMissingProperties()))
            {
                if (GUILayout.Button(ValidateLabel, GUILayout.Height(24f)))
                    ValidateSelectedAssets();
            }

            if (!string.IsNullOrEmpty(validationMessage))
                EditorGUILayout.HelpBox(validationMessage, validationMessageType);
        }

        private void DrawScriptReference()
        {
            if (script == null)
                return;

            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.PropertyField(script);
        }

        private void DrawSummary()
        {
            if (serializedObject.isEditingMultipleObjects)
            {
                EditorGUILayout.HelpBox(
                    "Multiple catalogs selected. Serialized list edits apply through Unity's multi-object editing path; validation checks every selected asset without writing changes.",
                    MessageType.Info);
                return;
            }

            if (HasMissingProperties())
            {
                EditorGUILayout.HelpBox("Catalog serialization properties are unavailable.", MessageType.Error);
                return;
            }

            RefreshSummaryIfNeeded();
            EditorGUILayout.HelpBox(summary, MessageType.None);
        }

        private static void DrawGroup(
            string title,
            SerializedProperty property,
            ref bool expanded)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            expanded = EditorGUILayout.Foldout(expanded, title, true);
            if (expanded)
            {
                if (property != null)
                {
                    using (new EditorGUI.IndentLevelScope())
                        EditorGUILayout.PropertyField(property, EntriesLabel, true);
                }
                else
                {
                    EditorGUILayout.HelpBox("Serialized group is unavailable.", MessageType.Error);
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void ValidateSelectedAssets()
        {
            int validCount = 0;
            ulong lastManifestHash = 0UL;
            int lastEntryCount = 0;

            for (int i = 0; i < targets.Length; i++)
            {
                var asset = targets[i] as GASNetworkContentCatalogAsset;
                if (asset == null)
                    continue;

                try
                {
                    GASNetworkContentCatalog catalog = asset.BuildCatalog();
                    lastManifestHash = catalog.ManifestHash;
                    lastEntryCount = catalog.Count;
                    validCount++;
                }
                catch (Exception exception)
                {
                    validationMessage = $"{asset.name}: {exception.Message}";
                    validationMessageType = MessageType.Error;
                    return;
                }
            }

            validationMessageType = MessageType.Info;
            validationMessage = targets.Length == 1
                ? $"Catalog is valid: {lastEntryCount} entries, manifest 0x{lastManifestHash:X16}."
                : $"All {validCount} selected catalogs are valid.";
        }

        private void RefreshSummaryIfNeeded()
        {
            int abilityCount = abilities.arraySize;
            int effectCount = effects.arraySize;
            int attributeCount = attributes.arraySize;
            int setByCallerCount = setByCallerNames.arraySize;
            int targetSurfaceCount = targetSurfaces.arraySize;
            if (abilityCount == lastAbilityCount &&
                effectCount == lastEffectCount &&
                attributeCount == lastAttributeCount &&
                setByCallerCount == lastSetByCallerCount &&
                targetSurfaceCount == lastTargetSurfaceCount)
            {
                return;
            }

            lastAbilityCount = abilityCount;
            lastEffectCount = effectCount;
            lastAttributeCount = attributeCount;
            lastSetByCallerCount = setByCallerCount;
            lastTargetSurfaceCount = targetSurfaceCount;
            int total = abilityCount + effectCount + attributeCount + setByCallerCount + targetSurfaceCount;
            summary = $"{total} registrations | Abilities {abilityCount} | Effects {effectCount} | Attributes {attributeCount} | SetByCaller {setByCallerCount} | Target Surfaces {targetSurfaceCount}";
        }

        private void InvalidateSummary()
        {
            lastAbilityCount = -1;
            lastEffectCount = -1;
            lastAttributeCount = -1;
            lastSetByCallerCount = -1;
            lastTargetSurfaceCount = -1;
        }

        private bool HasMissingProperties()
        {
            return abilities == null || effects == null || attributes == null ||
                   setByCallerNames == null || targetSurfaces == null;
        }
    }
}
