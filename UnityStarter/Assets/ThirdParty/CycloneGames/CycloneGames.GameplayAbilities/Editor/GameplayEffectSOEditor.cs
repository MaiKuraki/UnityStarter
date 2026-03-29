using UnityEditor;
using UnityEngine;
using System.Reflection;
using System.Collections.Generic;
using System.Text;

namespace CycloneGames.GameplayAbilities.Editor
{
    /// <summary>
    /// Custom editor for GameplayEffectSO and derived types.
    /// Provides organized layout with validation, summaries, and conditional field visibility.
    /// </summary>
    [CustomEditor(typeof(Runtime.GameplayEffectSO), true)]
    [CanEditMultipleObjects]
    public class GameplayEffectSOEditor : UnityEditor.Editor
    {
        private static readonly HashSet<string> s_BasePropertyNames = new HashSet<string>();
        private static bool s_BasePropertiesInitialized;

        // Core
        private SerializedProperty effectNameProp;
        private SerializedProperty durationPolicyProp;
        private SerializedProperty durationProp;
        private SerializedProperty periodProp;
        private SerializedProperty modifiersProp;
        private SerializedProperty executionProp;
        private SerializedProperty stackingProp;
        private SerializedProperty grantedAbilitiesProp;

        // Tags
        private SerializedProperty assetTagsProp;
        private SerializedProperty grantedTagsProp;
        private SerializedProperty appTagReqProp;
        private SerializedProperty ongoingTagReqProp;
        private SerializedProperty removeTagsProp;

        // Cosmetics
        private SerializedProperty cuesProp;
        private SerializedProperty suppressCuesProp;

        // Advanced
        private SerializedProperty removeAfterAbilityEndsProp;
        private SerializedProperty executePeriodicOnAppProp;
        private SerializedProperty overflowEffectsProp;
        private SerializedProperty denyOverflowProp;

        // Foldout states
        private bool showModifiers = true;
        private bool showTags = true;
        private bool showCosmetics = false;
        private bool showAdvanced = false;
        private bool showDerivedFields = true;
        private bool showSummary = true;

        // Styles
        private static GUIStyle s_SummaryStyle;
        private static GUIStyle s_SectionHeader;
        private static readonly Color s_WarningBg = new Color(1f, 0.9f, 0.5f, 0.15f);
        private static readonly Color s_SectionLine = new Color(0.3f, 0.3f, 0.3f, 1f);

        private readonly StringBuilder sb = new StringBuilder(256);

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
            suppressCuesProp = serializedObject.FindProperty("SuppressGameplayCues");

            removeAfterAbilityEndsProp = serializedObject.FindProperty("RemoveGameplayEffectsAfterAbilityEnds");
            executePeriodicOnAppProp = serializedObject.FindProperty("ExecutePeriodicEffectOnApplication");
            overflowEffectsProp = serializedObject.FindProperty("OverflowEffects");
            denyOverflowProp = serializedObject.FindProperty("DenyOverflowApplication");
        }

        private static void EnsureStyles()
        {
            if (s_SummaryStyle != null) return;

            s_SummaryStyle = new GUIStyle(EditorStyles.helpBox)
            {
                richText = true,
                fontSize = 11,
                padding = new RectOffset(8, 8, 6, 6)
            };

            s_SectionHeader = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12
            };
        }

        public override void OnInspectorGUI()
        {
            EnsureStyles();
            serializedObject.Update();

            var policy = (Runtime.EDurationPolicy)durationPolicyProp.enumValueIndex;

            // ═══════════════════════════════════════════
            // Summary Box
            // ═══════════════════════════════════════════
            showSummary = EditorGUILayout.Foldout(showSummary, "Effect Summary", true);
            if (showSummary)
            {
                DrawEffectSummary(policy);
            }

            EditorGUILayout.Space(4);

            // ═══════════════════════════════════════════
            // Validation Warnings
            // ═══════════════════════════════════════════
            DrawValidationWarnings(policy);

            // ═══════════════════════════════════════════
            // Effect Definition
            // ═══════════════════════════════════════════
            DrawSectionLine();
            EditorGUILayout.LabelField("Effect Definition", s_SectionHeader);

            EditorGUILayout.PropertyField(effectNameProp, new GUIContent("Name"));
            EditorGUILayout.PropertyField(durationPolicyProp, new GUIContent("Duration Policy"));

            // Duration & Timing — only for non-Instant
            if (policy != Runtime.EDurationPolicy.Instant)
            {
                EditorGUILayout.Space(4);
                EditorGUI.indentLevel++;

                if (policy == Runtime.EDurationPolicy.HasDuration)
                {
                    EditorGUILayout.PropertyField(durationProp, new GUIContent("Duration (sec)"));
                }

                EditorGUILayout.PropertyField(periodProp, new GUIContent("Period (sec)"));
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.PropertyField(stackingProp, new GUIContent("Stacking"), true);

            EditorGUILayout.Space(8);

            // ═══════════════════════════════════════════
            // Modifiers & Execution
            // ═══════════════════════════════════════════
            DrawSectionLine();
            int modCount = modifiersProp?.arraySize ?? 0;
            int abilityCount = grantedAbilitiesProp?.arraySize ?? 0;
            showModifiers = EditorGUILayout.Foldout(showModifiers,
                $"Modifiers & Execution  ({modCount} modifier{(modCount != 1 ? "s" : "")}, {abilityCount} granted abilit{(abilityCount != 1 ? "ies" : "y")})", true);

            if (showModifiers)
            {
                EditorGUI.indentLevel++;
                if (modifiersProp != null)
                    EditorGUILayout.PropertyField(modifiersProp, new GUIContent("Attribute Modifiers"), true);

                EditorGUILayout.PropertyField(executionProp, new GUIContent("Custom Execution"));
                EditorGUILayout.PropertyField(grantedAbilitiesProp, new GUIContent("Granted Abilities"), true);
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(4);

            // ═══════════════════════════════════════════
            // Tag Configuration
            // ═══════════════════════════════════════════
            DrawSectionLine();
            showTags = EditorGUILayout.Foldout(showTags, "Tag Configuration", true);

            if (showTags)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(assetTagsProp, new GUIContent("Asset Tags (describe effect)"));
                EditorGUILayout.PropertyField(grantedTagsProp, new GUIContent("Granted Tags (applied to target)"));
                EditorGUILayout.Space(4);
                EditorGUILayout.PropertyField(appTagReqProp, new GUIContent("Application Requirements"));
                EditorGUILayout.PropertyField(ongoingTagReqProp, new GUIContent("Ongoing Requirements"));
                EditorGUILayout.PropertyField(removeTagsProp, new GUIContent("Remove Effects With Tags"));
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(4);

            // ═══════════════════════════════════════════
            // Cosmetics
            // ═══════════════════════════════════════════
            DrawSectionLine();
            showCosmetics = EditorGUILayout.Foldout(showCosmetics, "Cosmetics (Cues)", true);

            if (showCosmetics)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(cuesProp, new GUIContent("Gameplay Cues"));
                EditorGUILayout.PropertyField(suppressCuesProp, new GUIContent("Suppress Cues"));
                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(4);

            // ═══════════════════════════════════════════
            // Advanced
            // ═══════════════════════════════════════════
            DrawSectionLine();
            showAdvanced = EditorGUILayout.Foldout(showAdvanced, "Advanced", true);

            if (showAdvanced)
            {
                EditorGUI.indentLevel++;

                EditorGUILayout.PropertyField(removeAfterAbilityEndsProp,
                    new GUIContent("Remove When Ability Ends", "Auto-remove this effect when the granting ability ends."));

                if (policy != Runtime.EDurationPolicy.Instant && periodProp.floatValue > 0)
                {
                    EditorGUILayout.PropertyField(executePeriodicOnAppProp,
                        new GUIContent("Execute On Application", "If true, the first periodic tick fires immediately on application."));
                }

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Overflow (Stack Limit)", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(overflowEffectsProp, new GUIContent("Overflow Effects"), true);
                EditorGUILayout.PropertyField(denyOverflowProp,
                    new GUIContent("Deny Overflow Application", "If true, prevent new application when at stack limit."));

                EditorGUILayout.Space(4);
                if (GUILayout.Button("Clear Runtime Cache", EditorStyles.miniButton))
                {
                    foreach (var t in targets)
                    {
                        if (t is Runtime.GameplayEffectSO effectSO)
                            effectSO.ClearCache();
                    }
                }
                EditorGUILayout.HelpBox("Clear cache if you modify values at runtime or after live changes.", MessageType.Info);

                EditorGUI.indentLevel--;
            }

            // ═══════════════════════════════════════════
            // Derived Class Fields
            // ═══════════════════════════════════════════
            DrawDerivedClassFields();

            serializedObject.ApplyModifiedProperties();
        }

        #region Summary

        private void DrawEffectSummary(Runtime.EDurationPolicy policy)
        {
            sb.Clear();

            string effectName = effectNameProp.stringValue;
            if (string.IsNullOrEmpty(effectName)) effectName = target.name;

            sb.Append("<b>").Append(effectName).Append("</b>");

            // Type
            switch (policy)
            {
                case Runtime.EDurationPolicy.Instant:
                    sb.Append("  \u2022  <color=#6BB8E0>Instant</color>");
                    break;
                case Runtime.EDurationPolicy.HasDuration:
                    sb.Append("  \u2022  <color=#4CA65F>Duration: ").Append(durationProp.floatValue.ToString("F1")).Append("s</color>");
                    break;
                case Runtime.EDurationPolicy.Infinite:
                    sb.Append("  \u2022  <color=#9B6CC0>Infinite</color>");
                    break;
            }

            // Period
            if (periodProp.floatValue > 0 && policy != Runtime.EDurationPolicy.Instant)
            {
                sb.Append("  \u2022  Period: ").Append(periodProp.floatValue.ToString("F2")).Append('s');
            }

            // Modifiers count
            int modCount = modifiersProp?.arraySize ?? 0;
            if (modCount > 0) sb.Append("\n").Append(modCount).Append(" modifier(s)");

            // Stacking
            var stackTypeProp = stackingProp.FindPropertyRelative("Type");
            if (stackTypeProp != null && stackTypeProp.enumValueIndex != 0)
            {
                var limitProp = stackingProp.FindPropertyRelative("Limit");
                sb.Append("  \u2022  Stacking: ").Append(((Runtime.EGameplayEffectStackingType)stackTypeProp.enumValueIndex).ToString());
                if (limitProp != null) sb.Append(" (max ").Append(limitProp.intValue).Append(')');
            }

            EditorGUILayout.LabelField(sb.ToString(), s_SummaryStyle);
        }

        #endregion

        #region Validation

        private void DrawValidationWarnings(Runtime.EDurationPolicy policy)
        {
            bool hasWarnings = false;

            // Name warning
            if (string.IsNullOrEmpty(effectNameProp.stringValue))
            {
                DrawWarningBox("Effect Name is empty. This makes debugging difficult.");
                hasWarnings = true;
            }

            // HasDuration with zero or negative duration
            if (policy == Runtime.EDurationPolicy.HasDuration && durationProp.floatValue <= 0f)
            {
                DrawWarningBox("Duration Policy is 'HasDuration' but Duration is \u2264 0. The effect will expire immediately.");
                hasWarnings = true;
            }

            // Period on Instant (meaningless)
            if (policy == Runtime.EDurationPolicy.Instant && periodProp.floatValue > 0f)
            {
                DrawWarningBox("Period is set but Duration Policy is 'Instant'. Period has no effect on Instant effects.");
                hasWarnings = true;
            }

            // Stacking on Instant (meaningless)
            var stackTypeProp = stackingProp.FindPropertyRelative("Type");
            if (policy == Runtime.EDurationPolicy.Instant && stackTypeProp != null && stackTypeProp.enumValueIndex != 0)
            {
                DrawWarningBox("Stacking is configured but Duration Policy is 'Instant'. Instant effects cannot stack.");
                hasWarnings = true;
            }

            // Stacking limit of 0 or negative
            if (stackTypeProp != null && stackTypeProp.enumValueIndex != 0)
            {
                var limitProp = stackingProp.FindPropertyRelative("Limit");
                if (limitProp != null && limitProp.intValue <= 0)
                {
                    DrawWarningBox("Stacking limit is \u2264 0. This may cause unexpected behavior.");
                    hasWarnings = true;
                }
            }

            // Overflow effects configured but no stacking
            if (overflowEffectsProp != null && overflowEffectsProp.arraySize > 0
                && (stackTypeProp == null || stackTypeProp.enumValueIndex == 0))
            {
                DrawWarningBox("Overflow Effects are configured but Stacking is 'None'. Overflow only triggers at stack limit.");
                hasWarnings = true;
            }

            if (hasWarnings) EditorGUILayout.Space(4);
        }

        private static void DrawWarningBox(string message)
        {
            EditorGUILayout.HelpBox(message, MessageType.Warning);
        }

        #endregion

        #region Derived Fields

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
                DrawSectionLine();
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

        #endregion

        #region Helpers

        private static void DrawSectionLine()
        {
            EditorGUILayout.Space(2);
            Rect lineRect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(lineRect, s_SectionLine);
            EditorGUILayout.Space(2);
        }

        #endregion
    }
}
