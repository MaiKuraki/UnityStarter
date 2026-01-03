using UnityEditor;
using UnityEngine;
using System.Reflection;
using System.Collections.Generic;

namespace CycloneGames.GameplayAbilities.Editor
{
    /// <summary>
    /// Custom editor for GameplayEffectSO and derived types.
    /// </summary>
    [CustomEditor(typeof(Runtime.GameplayEffectSO), true)]
    [CanEditMultipleObjects]
    public class GameplayEffectSOEditor : UnityEditor.Editor
    {
        private static readonly HashSet<string> s_BasePropertyNames = new HashSet<string>();
        private static bool s_BasePropertiesInitialized;

        private SerializedProperty effectNameProp;
        private SerializedProperty durationPolicyProp;
        private SerializedProperty durationProp;
        private SerializedProperty periodProp;
        private SerializedProperty modifiersProp;
        private SerializedProperty executionProp;
        private SerializedProperty stackingProp;
        private SerializedProperty grantedAbilitiesProp;
        private SerializedProperty assetTagsProp;
        private SerializedProperty grantedTagsProp;
        private SerializedProperty appTagReqProp;
        private SerializedProperty ongoingTagReqProp;
        private SerializedProperty removeTagsProp;
        private SerializedProperty cuesProp;

        private bool showModifiers = true;
        private bool showTags = true;
        private bool showAdvanced = false;
        private bool showDerivedFields = true;

        private void OnEnable()
        {
            CacheBasePropertyNames();
            CacheProperties();
        }

        private static void CacheBasePropertyNames()
        {
            if (s_BasePropertiesInitialized) return;

            var baseType = typeof(Runtime.GameplayEffectSO);
            foreach (var field in baseType.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                s_BasePropertyNames.Add(field.Name);
            }
            s_BasePropertiesInitialized = true;
        }

        private void CacheProperties()
        {
            effectNameProp = serializedObject.FindProperty("EffectName");
            durationPolicyProp = serializedObject.FindProperty("DurationPolicy");
            durationProp = serializedObject.FindProperty("Duration");
            periodProp = serializedObject.FindProperty("Period");
            modifiersProp = serializedObject.FindProperty("SerializableModifiers");
            executionProp = serializedObject.FindProperty("ExecutionDefinition");
            stackingProp = serializedObject.FindProperty("Stacking");
            grantedAbilitiesProp = serializedObject.FindProperty("GrantedAbilities");
            assetTagsProp = serializedObject.FindProperty("AssetTags");
            grantedTagsProp = serializedObject.FindProperty("GrantedTags");
            appTagReqProp = serializedObject.FindProperty("ApplicationTagRequirements");
            ongoingTagReqProp = serializedObject.FindProperty("OngoingTagRequirements");
            removeTagsProp = serializedObject.FindProperty("RemoveGameplayEffectsWithTags");
            cuesProp = serializedObject.FindProperty("GameplayCues");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Effect Definition Section
            EditorGUILayout.LabelField("Effect Definition", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(effectNameProp, new GUIContent("Name"));
            EditorGUILayout.PropertyField(durationPolicyProp, new GUIContent("Duration Policy"));

            EditorGUILayout.Space(8);

            // Duration & Timing Section - only show for non-Instant effects
            var policy = (Runtime.EDurationPolicy)durationPolicyProp.enumValueIndex;

            if (policy != Runtime.EDurationPolicy.Instant)
            {
                EditorGUILayout.LabelField("Duration & Timing", EditorStyles.boldLabel);

                if (policy == Runtime.EDurationPolicy.HasDuration)
                {
                    EditorGUILayout.PropertyField(durationProp, new GUIContent("Duration (seconds)"));
                }

                EditorGUILayout.PropertyField(periodProp, new GUIContent("Period (seconds)"));
                EditorGUILayout.Space(4);
            }

            EditorGUILayout.PropertyField(stackingProp, new GUIContent("Stacking"), true);

            EditorGUILayout.Space(8);

            // Modifiers Section
            showModifiers = EditorGUILayout.Foldout(showModifiers,
                $"Modifiers ({modifiersProp?.arraySize ?? 0})", true);

            if (showModifiers)
            {
                EditorGUI.indentLevel++;
                if (modifiersProp != null)
                {
                    EditorGUILayout.PropertyField(modifiersProp, new GUIContent("Attribute Modifiers"), true);
                }
                EditorGUILayout.PropertyField(executionProp, new GUIContent("Custom Execution"));
                EditorGUILayout.PropertyField(grantedAbilitiesProp, new GUIContent("Granted Abilities"), true);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(4);

            // Tag Configuration Section
            showTags = EditorGUILayout.Foldout(showTags, "Tag Configuration", true);

            if (showTags)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(assetTagsProp, new GUIContent("Asset Tags"));
                EditorGUILayout.PropertyField(grantedTagsProp, new GUIContent("Granted Tags"));
                EditorGUILayout.PropertyField(cuesProp, new GUIContent("Gameplay Cues"));
                EditorGUILayout.Space(4);
                EditorGUILayout.PropertyField(appTagReqProp, new GUIContent("Application Requirements"));
                EditorGUILayout.PropertyField(ongoingTagReqProp, new GUIContent("Ongoing Requirements"));
                EditorGUILayout.PropertyField(removeTagsProp, new GUIContent("Remove Effects With Tags"));
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(4);

            // Advanced Section
            showAdvanced = EditorGUILayout.Foldout(showAdvanced, "Advanced", true);

            if (showAdvanced)
            {
                EditorGUI.indentLevel++;
                if (GUILayout.Button("Clear Runtime Cache"))
                {
                    foreach (var t in targets)
                    {
                        if (t is Runtime.GameplayEffectSO effectSO)
                        {
                            effectSO.ClearCache();
                        }
                    }
                }
                EditorGUILayout.HelpBox("Clear cache if you modify values at runtime.", MessageType.Info);
                EditorGUI.indentLevel--;
            }

            // Derived Class Fields
            DrawDerivedClassFields();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawDerivedClassFields()
        {
            var targetType = target.GetType();
            if (targetType == typeof(Runtime.GameplayEffectSO)) return;

            var derivedFields = new List<SerializedProperty>();
            var iterator = serializedObject.GetIterator();
            bool enterChildren = true;

            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (iterator.name == "m_Script") continue;
                if (s_BasePropertyNames.Contains(iterator.name)) continue;
                derivedFields.Add(iterator.Copy());
            }

            if (derivedFields.Count > 0)
            {
                EditorGUILayout.Space(8);
                showDerivedFields = EditorGUILayout.Foldout(showDerivedFields,
                    $"Custom Fields ({targetType.Name})", true);

                if (showDerivedFields)
                {
                    EditorGUI.indentLevel++;
                    foreach (var prop in derivedFields)
                    {
                        EditorGUILayout.PropertyField(prop, true);
                    }
                    EditorGUI.indentLevel--;
                }
            }
        }
    }
}
