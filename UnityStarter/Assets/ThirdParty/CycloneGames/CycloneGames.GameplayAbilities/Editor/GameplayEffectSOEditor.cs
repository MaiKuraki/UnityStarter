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
        private SerializedProperty durationMagnitudeProp;
        private SerializedProperty periodProp;
        private SerializedProperty modifiersProp;
        private SerializedProperty executionProp;
        private SerializedProperty stackingProp;
        private SerializedProperty grantedAbilitiesProp;
        private SerializedProperty grantedAbilityRemovalPoliciesProp;

        // Tags
        private SerializedProperty assetTagsProp;
        private SerializedProperty grantedTagsProp;
        private SerializedProperty blockedAbilityTagsProp;
        private SerializedProperty appTagReqProp;
        private SerializedProperty ongoingTagReqProp;
        private SerializedProperty removeTagsProp;

        // Cosmetics
        private SerializedProperty cuesProp;
        private SerializedProperty suppressCuesProp;

        // Advanced
        private SerializedProperty removeAfterAbilityEndsProp;
        private SerializedProperty executePeriodicOnAppProp;
        private SerializedProperty periodicInhibitionPolicyProp;
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
        private static readonly GUIContent s_NameContent = new GUIContent("Name");
        private static readonly GUIContent s_DurationPolicyContent = new GUIContent("Duration Policy");
        private static readonly GUIContent s_DurationContent = new GUIContent("Duration (sec)");
        private static readonly GUIContent s_DurationMagnitudeContent = new GUIContent(
            "Duration Magnitude",
            "Optional data-driven duration source. When enabled, it overrides the static Duration value for every spec.");
        private static readonly GUIContent s_PeriodContent = new GUIContent("Period (sec)");
        private static readonly GUIContent s_StackingContent = new GUIContent("Stacking");
        private static readonly GUIContent s_ModifiersContent = new GUIContent("Attribute Modifiers");
        private static readonly GUIContent s_ExecutionContent = new GUIContent("Custom Execution");
        private static readonly GUIContent s_GrantedAbilitiesContent = new GUIContent("Granted Abilities");
        private static readonly GUIContent s_AssetTagsContent = new GUIContent("Asset Tags (describe effect)");
        private static readonly GUIContent s_GrantedTagsContent = new GUIContent("Granted Tags (applied to target)");
        private static readonly GUIContent s_BlockedAbilityTagsContent = new GUIContent(
            "Blocked Ability Tags",
            "Ability tags blocked while this effect contributes. Withdrawn while the effect is inhibited.");
        private static readonly GUIContent s_GrantedAbilityPoliciesContent = new GUIContent(
            "Granted Ability Removal Policies",
            "Per-grant policy applied when the effect is removed or inhibited. Entries beyond the list default to CancelAbilityImmediately.");
        private static readonly GUIContent s_ApplicationRequirementsContent = new GUIContent("Application Requirements");
        private static readonly GUIContent s_OngoingRequirementsContent = new GUIContent("Ongoing Requirements");
        private static readonly GUIContent s_RemoveEffectsContent = new GUIContent("Remove Effects With Tags");
        private static readonly GUIContent s_GameplayCuesContent = new GUIContent("Gameplay Cues");
        private static readonly GUIContent s_SuppressCuesContent = new GUIContent("Suppress Cues");
        private static readonly GUIContent s_RemoveOnAbilityEndContent = new GUIContent(
            "Remove When Ability Ends",
            "Automatically removes this effect when the granting ability ends.");
        private static readonly GUIContent s_ExecuteOnApplicationContent = new GUIContent(
            "Execute On Application",
            "Executes the effect once immediately on application, in addition to the regular period schedule.");
        private static readonly GUIContent s_PeriodicInhibitionContent = new GUIContent(
            "Periodic Inhibition Policy",
            "How the period responds when ongoing tag requirements become satisfied again.");
        private static readonly GUIContent s_OverflowEffectsContent = new GUIContent("Overflow Effects");
        private static readonly GUIContent s_DenyOverflowContent = new GUIContent(
            "Deny Overflow Application",
            "Prevents the attempted application when the stack is already at its limit.");

        private readonly StringBuilder sb = new StringBuilder(256);
        private readonly List<SerializedProperty> derivedProperties = new List<SerializedProperty>(8);
        private SerializedProperty stackingTypeProp;
        private SerializedProperty stackingLimitProp;
        private SerializedProperty limitModeProp;
        private SerializedProperty magnitudePolicyProp;
        private string derivedFieldsLabel;
        private string modifiersFoldoutLabel;
        private int modifiersFoldoutCount = -1;
        private int grantedAbilitiesFoldoutCount = -1;

        private void OnEnable()
        {
            CacheBasePropertyNames();
            CacheProperties();
            CacheDerivedProperties();
        }

        private static void CacheBasePropertyNames()
        {
            if (s_BasePropertiesInitialized) return;

            var baseType = typeof(Runtime.GameplayEffectSO);
            foreach (var field in baseType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (IsUnitySerializedField(field))
                {
                    s_BasePropertyNames.Add(field.Name);
                }
            }
            s_BasePropertiesInitialized = true;
        }

        private static bool IsUnitySerializedField(FieldInfo field)
        {
            return !field.IsStatic
                && !field.IsNotSerialized
                && (field.IsPublic || field.GetCustomAttribute<SerializeField>() != null);
        }

        private void CacheProperties()
        {
            effectNameProp = serializedObject.FindProperty("EffectName");
            durationPolicyProp = serializedObject.FindProperty("DurationPolicy");
            durationProp = serializedObject.FindProperty("Duration");
            durationMagnitudeProp = serializedObject.FindProperty("DurationMagnitude");
            periodProp = serializedObject.FindProperty("Period");
            modifiersProp = serializedObject.FindProperty("SerializableModifiers");
            executionProp = serializedObject.FindProperty("ExecutionDefinition");
            stackingProp = serializedObject.FindProperty("Stacking");
            grantedAbilitiesProp = serializedObject.FindProperty("GrantedAbilities");
            grantedAbilityRemovalPoliciesProp = serializedObject.FindProperty("GrantedAbilityRemovalPolicies");

            assetTagsProp = serializedObject.FindProperty("AssetTags");
            grantedTagsProp = serializedObject.FindProperty("GrantedTags");
            blockedAbilityTagsProp = serializedObject.FindProperty("BlockedAbilityTags");
            appTagReqProp = serializedObject.FindProperty("ApplicationTagRequirements");
            ongoingTagReqProp = serializedObject.FindProperty("OngoingTagRequirements");
            removeTagsProp = serializedObject.FindProperty("RemoveGameplayEffectsWithTags");

            cuesProp = serializedObject.FindProperty("GameplayCues");
            suppressCuesProp = serializedObject.FindProperty("SuppressGameplayCues");

            removeAfterAbilityEndsProp = serializedObject.FindProperty("RemoveGameplayEffectsAfterAbilityEnds");
            executePeriodicOnAppProp = serializedObject.FindProperty("ExecutePeriodicEffectOnApplication");
            periodicInhibitionPolicyProp = serializedObject.FindProperty("PeriodicInhibitionPolicy");
            overflowEffectsProp = serializedObject.FindProperty("OverflowEffects");
            denyOverflowProp = serializedObject.FindProperty("DenyOverflowApplication");
            stackingTypeProp = stackingProp?.FindPropertyRelative("Type");
            stackingLimitProp = stackingProp?.FindPropertyRelative("Limit");
            limitModeProp = stackingProp?.FindPropertyRelative("LimitMode");
            magnitudePolicyProp = stackingProp?.FindPropertyRelative("MagnitudePolicy");
        }

        private void CacheDerivedProperties()
        {
            derivedProperties.Clear();
            var iterator = serializedObject.GetIterator();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (iterator.name == "m_Script" || s_BasePropertyNames.Contains(iterator.name))
                {
                    continue;
                }

                derivedProperties.Add(iterator.Copy());
            }

            derivedFieldsLabel = target != null
                ? $"Custom Fields ({target.GetType().Name})"
                : "Custom Fields";
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

            bool mixedDurationPolicy = durationPolicyProp.hasMultipleDifferentValues;
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

            EditorGUILayout.PropertyField(effectNameProp, s_NameContent);
            EditorGUILayout.PropertyField(durationPolicyProp, s_DurationPolicyContent);

            // Duration & Timing — only for non-Instant
            if (mixedDurationPolicy || policy != Runtime.EDurationPolicy.Instant)
            {
                EditorGUILayout.Space(4);
                EditorGUI.indentLevel++;

                if (mixedDurationPolicy || policy == Runtime.EDurationPolicy.HasDuration)
                {
                    EditorGUILayout.PropertyField(durationProp, s_DurationContent);
                    if (durationMagnitudeProp != null)
                    {
                        EditorGUILayout.PropertyField(durationMagnitudeProp, s_DurationMagnitudeContent, true);
                    }
                }

                EditorGUILayout.PropertyField(periodProp, s_PeriodContent);

                if (!serializedObject.isEditingMultipleObjects && periodProp.floatValue > 0f)
                {
                    GameplayEffectTimingPreview.Draw(
                        Runtime.GameplayEffectPeriodSchedule.Compute(
                            policy,
                            durationProp.floatValue,
                            periodProp.floatValue,
                            executePeriodicOnAppProp != null && executePeriodicOnAppProp.boolValue),
                        durationProp.floatValue,
                        HasAnyTagsConfigured(ongoingTagReqProp),
                        stackingTypeProp != null && stackingTypeProp.enumValueIndex != 0);
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.PropertyField(stackingProp, s_StackingContent, true);

            EditorGUILayout.Space(8);

            // ═══════════════════════════════════════════
            // Modifiers & Execution
            // ═══════════════════════════════════════════
            DrawSectionLine();
            int modCount = modifiersProp?.arraySize ?? 0;
            int abilityCount = grantedAbilitiesProp?.arraySize ?? 0;
            UpdateModifiersFoldoutLabel(modCount, abilityCount);
            showModifiers = EditorGUILayout.Foldout(showModifiers, modifiersFoldoutLabel, true);

            if (showModifiers)
            {
                EditorGUI.indentLevel++;
                if (modifiersProp != null)
                    EditorGUILayout.PropertyField(modifiersProp, s_ModifiersContent, true);

                DrawCustomCalculationSummary();

                EditorGUILayout.PropertyField(executionProp, s_ExecutionContent);
                EditorGUILayout.PropertyField(grantedAbilitiesProp, s_GrantedAbilitiesContent, true);
                DrawGrantedAbilityPolicyList();
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
                EditorGUILayout.LabelField("Tag Relationships", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(assetTagsProp, s_AssetTagsContent);
                EditorGUILayout.PropertyField(grantedTagsProp, s_GrantedTagsContent);
                EditorGUILayout.PropertyField(blockedAbilityTagsProp, s_BlockedAbilityTagsContent);
                EditorGUILayout.Space(4);
                EditorGUILayout.PropertyField(appTagReqProp, s_ApplicationRequirementsContent);
                EditorGUILayout.PropertyField(ongoingTagReqProp, s_OngoingRequirementsContent);
                EditorGUILayout.PropertyField(removeTagsProp, s_RemoveEffectsContent);
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
                EditorGUILayout.PropertyField(cuesProp, s_GameplayCuesContent);
                EditorGUILayout.PropertyField(suppressCuesProp, s_SuppressCuesContent);
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

                EditorGUILayout.PropertyField(removeAfterAbilityEndsProp, s_RemoveOnAbilityEndContent);

                if (mixedDurationPolicy || (policy != Runtime.EDurationPolicy.Instant && periodProp.floatValue > 0))
                {
                    EditorGUILayout.PropertyField(executePeriodicOnAppProp, s_ExecuteOnApplicationContent);

                    bool hasOngoingRequirements = HasAnyOngoingRequirements();
                    using (new EditorGUI.DisabledScope(!hasOngoingRequirements))
                    {
                        EditorGUILayout.PropertyField(periodicInhibitionPolicyProp, s_PeriodicInhibitionContent);
                    }

                    if (!hasOngoingRequirements)
                    {
                        EditorGUILayout.HelpBox(
                            "Periodic inhibition policy only takes effect when Ongoing Requirements contain tags.",
                            MessageType.None);
                    }
                }

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Overflow (Stack Limit)", EditorStyles.boldLabel);
                EditorGUILayout.PropertyField(overflowEffectsProp, s_OverflowEffectsContent, true);
                EditorGUILayout.PropertyField(denyOverflowProp, s_DenyOverflowContent);

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

        private void DrawCustomCalculationSummary()
        {
            if (serializedObject == null)
            {
                return;
            }

            SerializedProperty modifiers = serializedObject.FindProperty("SerializableModifiers");
            if (modifiers == null || !modifiers.isArray)
            {
                return;
            }

            int missingAssetCount = 0;
            for (int i = 0; i < modifiers.arraySize; i++)
            {
                SerializedProperty element = modifiers.GetArrayElementAtIndex(i);
                SerializedProperty type = element.FindPropertyRelative("MagnitudeCalculationType");
                SerializedProperty calculation = element.FindPropertyRelative("CustomCalculation");
                if (type != null && calculation != null &&
                    type.enumValueIndex == (int)Runtime.EGameplayEffectMagnitudeCalculation.CustomCalculation &&
                    calculation.objectReferenceValue == null)
                {
                    missingAssetCount++;
                }
            }

            if (missingAssetCount > 0)
            {
                EditorGUILayout.HelpBox(
                    $"{missingAssetCount} modifier(s) use Custom Calculation without an asset; they use Fallback Magnitude.",
                    MessageType.Warning);
            }
        }

        private bool HasAnyOngoingRequirements()
        {
            UnityEngine.Object[] selected = serializedObject.targetObjects;
            for (int i = 0; i < selected.Length; i++)
            {
                if (selected[i] is Runtime.GameplayEffectSO effect &&
                    effect.OngoingTagRequirements != null &&
                    !effect.OngoingTagRequirements.IsEmpty)
                {
                    return true;
                }
            }

            return false;
        }

        private void DrawGrantedAbilityPolicyList()
        {
            if (grantedAbilityRemovalPoliciesProp == null)
            {
                return;
            }

            int abilityCount = grantedAbilitiesProp?.arraySize ?? 0;
            int policyCount = grantedAbilityRemovalPoliciesProp.arraySize;
            if (abilityCount == 0)
            {
                if (policyCount == 0)
                {
                    return;
                }

                EditorGUILayout.HelpBox(
                    "This effect grants no abilities, so removal policies have no effect.",
                    MessageType.Info);
                if (GUILayout.Button("Clear Policies", EditorStyles.miniButton, GUILayout.Width(120f)))
                {
                    grantedAbilityRemovalPoliciesProp.arraySize = 0;
                }
                return;
            }

            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField(s_GrantedAbilityPoliciesContent, EditorStyles.boldLabel);

            for (int i = 0; i < policyCount; i++)
            {
                SerializedProperty entry = grantedAbilityRemovalPoliciesProp.GetArrayElementAtIndex(i);
                EditorGUILayout.PropertyField(entry, new GUIContent(GetGrantedAbilityLabel(i, abilityCount)));
            }

            if (policyCount != abilityCount)
            {
                EditorGUILayout.HelpBox(
                    "Policy count does not match Granted Abilities. Missing entries default to CancelAbilityImmediately.",
                    MessageType.Warning);
            }

            if (GUILayout.Button("Match Granted Abilities", EditorStyles.miniButton, GUILayout.Width(170f)))
            {
                grantedAbilityRemovalPoliciesProp.arraySize = abilityCount;
            }
        }

        private string GetGrantedAbilityLabel(int index, int abilityCount)
        {
            if (index >= abilityCount)
            {
                return $"Unmatched policy {index}";
            }

            SerializedProperty ability = grantedAbilitiesProp?.GetArrayElementAtIndex(index);
            Object reference = ability?.objectReferenceValue;
            return reference != null ? reference.name : $"Granted Ability {index} (unassigned)";
        }

        #region Summary

        private void DrawEffectSummary(Runtime.EDurationPolicy policy)
        {
            sb.Clear();

            if (serializedObject.isEditingMultipleObjects)
            {
                sb.Append(targets.Length).Append(" effects selected");
                EditorGUILayout.LabelField(sb.ToString(), s_SummaryStyle);
                return;
            }

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
                    if (IsDurationMagnitudeEnabled())
                    {
                        sb.Append("  \u2022  <color=#4CA65F>Duration: data-driven (")
                            .Append(GetDurationMagnitudeTypeLabel()).Append(")</color>");
                    }
                    else
                    {
                        sb.Append("  \u2022  <color=#4CA65F>Duration: ").Append(durationProp.floatValue.ToString("F1")).Append("s</color>");
                    }
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
            if (stackingTypeProp != null && stackingTypeProp.enumValueIndex != 0)
            {
                string stackingLabel = ((Runtime.EGameplayEffectStackingType)stackingTypeProp.enumValueIndex) switch
                {
                    Runtime.EGameplayEffectStackingType.AggregateBySource => "AggregateBySource",
                    Runtime.EGameplayEffectStackingType.AggregateByTarget => "AggregateByTarget",
                    _ => "?"
                };
                sb.Append("  \u2022  Stacking: ").Append(stackingLabel);
                if (limitModeProp != null &&
                    limitModeProp.enumValueIndex == (int)Runtime.EGameplayEffectStackLimitMode.Unlimited)
                {
                    sb.Append(" (unlimited)");
                }
                else if (stackingLimitProp != null)
                {
                    sb.Append(stackingLimitProp.intValue > 0
                        ? " (max " + stackingLimitProp.intValue + ")"
                        : " (invalid limit)");
                }
                if (magnitudePolicyProp != null)
                {
                    sb.Append(magnitudePolicyProp.enumValueIndex == (int)Core.GASStackMagnitudePolicy.Exponential
                        ? ", exponential"
                        : ", linear");
                }
            }

            EditorGUILayout.LabelField(sb.ToString(), s_SummaryStyle);
        }

        #endregion

        #region Validation

        private void DrawValidationWarnings(Runtime.EDurationPolicy policy)
        {
            if (serializedObject.isEditingMultipleObjects)
            {
                return;
            }

            bool hasWarnings = false;

            // Name warning
            if (string.IsNullOrEmpty(effectNameProp.stringValue))
            {
                DrawWarningBox("Effect Name is empty. This makes debugging difficult.");
                hasWarnings = true;
            }

            // HasDuration with zero or negative duration and no data-driven source
            if (policy == Runtime.EDurationPolicy.HasDuration && durationProp.floatValue <= 0f && !IsDurationMagnitudeEnabled())
            {
                DrawWarningBox("Duration Policy is 'HasDuration' but Duration is \u2264 0 and no Duration Magnitude is enabled. The effect will expire immediately.");
                hasWarnings = true;
            }

            if (IsDurationMagnitudeEnabled() && policy != Runtime.EDurationPolicy.HasDuration)
            {
                DrawWarningBox("Duration Magnitude is enabled but Duration Policy is not 'HasDuration'. The runtime definition rejects this combination.");
                hasWarnings = true;
            }

            // Duration Magnitude SetByCaller without any key
            if (IsDurationMagnitudeEnabled())
            {
                var calculationTypeProp = durationMagnitudeProp.FindPropertyRelative("CalculationType");
                if (calculationTypeProp != null &&
                    calculationTypeProp.enumValueIndex == (int)Runtime.EGameplayEffectMagnitudeCalculation.SetByCaller)
                {
                    string tagName = durationMagnitudeProp.FindPropertyRelative("SetByCallerDataTag")?.FindPropertyRelative("tagName")?.stringValue;
                    string dataName = durationMagnitudeProp.FindPropertyRelative("SetByCallerDataName")?.stringValue;
                    if (string.IsNullOrEmpty(tagName) && string.IsNullOrEmpty(dataName))
                    {
                        DrawWarningBox("Duration Magnitude is SetByCaller but neither a Data Tag nor a Data Name is set. The default value is always used.");
                        hasWarnings = true;
                    }
                }
            }

            // ExtendDuration stacking requires ClearEntireStack expiration
            if (stackingTypeProp != null && stackingTypeProp.enumValueIndex != 0)
            {
                var stackingDurationPolicyProp = stackingProp.FindPropertyRelative("DurationPolicy");
                var stackingExpirationPolicyProp = stackingProp.FindPropertyRelative("ExpirationPolicy");
                if (stackingDurationPolicyProp != null && stackingExpirationPolicyProp != null &&
                    stackingDurationPolicyProp.enumValueIndex == (int)Runtime.EGameplayEffectStackingDurationPolicy.ExtendDuration &&
                    stackingExpirationPolicyProp.enumValueIndex != (int)Runtime.EGameplayEffectStackingExpirationPolicy.ClearEntireStack)
                {
                    DrawWarningBox("ExtendDuration stacking requires ClearEntireStack expiration. The runtime definition rejects this combination.");
                    hasWarnings = true;
                }
            }

            // Period on Instant (meaningless)
            if (policy == Runtime.EDurationPolicy.Instant && periodProp.floatValue > 0f)
            {
                DrawWarningBox("Period is set but Duration Policy is 'Instant'. Period has no effect on Instant effects.");
                hasWarnings = true;
            }

            // Stacking on Instant (meaningless)
            if (policy == Runtime.EDurationPolicy.Instant && stackingTypeProp != null && stackingTypeProp.enumValueIndex != 0)
            {
                DrawWarningBox("Stacking is configured but Duration Policy is 'Instant'. Instant effects cannot stack.");
                hasWarnings = true;
            }

            // Stacking limit validation
            if (stackingTypeProp != null && stackingTypeProp.enumValueIndex != 0)
            {
                bool unlimited = limitModeProp != null &&
                    limitModeProp.enumValueIndex == (int)Runtime.EGameplayEffectStackLimitMode.Unlimited;

                if (!unlimited && stackingLimitProp != null && stackingLimitProp.intValue <= 0)
                {
                    EditorGUILayout.HelpBox(
                        "Stack Limit must be at least 1 when the limit mode is Limited. Building this " +
                        "effect's runtime definition throws ArgumentOutOfRangeException until it is fixed.",
                        MessageType.Error);
                    if (GUILayout.Button("Set Stack Limit to 1"))
                    {
                        // Flushed by the ApplyModifiedProperties() at the end of OnInspectorGUI.
                        stackingLimitProp.intValue = 1;
                    }

                    hasWarnings = true;
                }
                else if (!unlimited && stackingLimitProp != null &&
                         stackingLimitProp.intValue > Runtime.GameplayEffect.MaxStackCount)
                {
                    DrawWarningBox(
                        $"Stack Limit exceeds {Runtime.GameplayEffect.MaxStackCount}. The runtime definition " +
                        "rejects it; use Stack Limit Mode = Unlimited instead of a huge number.");
                    hasWarnings = true;
                }

                int stackLimit = unlimited
                    ? ushort.MaxValue
                    : (stackingLimitProp != null ? stackingLimitProp.intValue : 0);

                // Mirrors GameplayEffect.ValidateStackMagnitudeRange so the asset fails here instead
                // of throwing when the runtime definition is built.
                if (magnitudePolicyProp != null &&
                    magnitudePolicyProp.enumValueIndex == (int)Core.GASStackMagnitudePolicy.Exponential &&
                    stackLimit > 1 &&
                    TryGetExponentialOverflow(stackLimit, out int maxSafeStacks))
                {
                    DrawWarningBox(
                        $"Exponential stacking overflows the Q32.32 range (2.1e9) before the Stack Limit. " +
                        $"With this effect's magnitudes the highest safe limit is {maxSafeStacks} stack(s). " +
                        "Lower the Stack Limit or switch to LinearBias; the runtime definition rejects this asset.");
                    hasWarnings = true;
                }
            }

            // Overflow effects configured but no stacking
            if (overflowEffectsProp != null && overflowEffectsProp.arraySize > 0
                && (stackingTypeProp == null || stackingTypeProp.enumValueIndex == 0))
            {
                DrawWarningBox("Overflow Effects are configured but Stacking is 'None'. Overflow only triggers at stack limit.");
                hasWarnings = true;
            }

            Runtime.GameplayEffectPeriodSchedule schedule = Runtime.GameplayEffectPeriodSchedule.Compute(
                policy,
                durationProp.floatValue,
                periodProp.floatValue,
                executePeriodicOnAppProp != null && executePeriodicOnAppProp.boolValue);

            if (schedule.IsPeriodic)
            {
                if (schedule.ExecutionCount == 0)
                {
                    DrawWarningBox(
                        "Period is longer than the duration and Execute On Application is off. The effect never executes.");
                    hasWarnings = true;
                }
                else if (!schedule.IsUnbounded && schedule.Period > durationProp.floatValue)
                {
                    DrawWarningBox(string.Concat(
                        "Period (", schedule.Period.ToString("F2"),
                        "s) is longer than Duration (", durationProp.floatValue.ToString("F2"),
                        "s). The periodic schedule never completes, so only the on-application tick fires."));
                    hasWarnings = true;
                }

                if (schedule.Period < 0.05f)
                {
                    DrawWarningBox(
                        "Period is below 0.05s. A single slow frame can queue several executions, bounded by MaxPeriodicEffectExecutionsPerTick.");
                    hasWarnings = true;
                }
            }

            if (hasWarnings) EditorGUILayout.Space(4);
        }

        private static bool HasAnyTagsConfigured(SerializedProperty requirements)
        {
            return requirements != null &&
                   (CountConfiguredTags(requirements, "forbiddenTags") > 0 ||
                    CountConfiguredTags(requirements, "requiredTags") > 0);
        }

        private static int CountConfiguredTags(SerializedProperty requirements, string containerField)
        {
            SerializedProperty container = requirements.FindPropertyRelative(containerField);
            if (container == null) return 0;

            SerializedProperty names = container.FindPropertyRelative("explicitTagNames");
            return names != null ? names.arraySize : 0;
        }

        /// <summary>
        /// <summary>
        /// Detects whether exponential stacking leaves the Q32.32 range before reaching the stack
        /// limit, and reports the highest limit that still fits. AttributeBased, SetByCaller and
        /// CustomCalculation magnitudes are not statically known and are skipped, so the answer is a
        /// lower bound of the real worst case.
        /// </summary>
        private bool TryGetExponentialOverflow(int stackLimit, out int maxSafeStacks)
        {
            const double MaxRepresentable = 2147483647.0;
            maxSafeStacks = int.MaxValue;

            if (modifiersProp == null)
            {
                return false;
            }

            double combinedFactor = 1.0;
            for (int i = 0; i < modifiersProp.arraySize; i++)
            {
                SerializedProperty modifier = modifiersProp.GetArrayElementAtIndex(i);
                SerializedProperty operation = modifier.FindPropertyRelative("Operation");
                SerializedProperty calculation = modifier.FindPropertyRelative("MagnitudeCalculationType");
                if (operation == null || calculation == null)
                {
                    continue;
                }

                var op = (Runtime.EAttributeModifierOperation)operation.enumValueIndex;
                if (op != Runtime.EAttributeModifierOperation.Multiply &&
                    op != Runtime.EAttributeModifierOperation.Division)
                {
                    continue;
                }

                if ((Runtime.EGameplayEffectMagnitudeCalculation)calculation.enumValueIndex !=
                    Runtime.EGameplayEffectMagnitudeCalculation.ScalableFloat)
                {
                    continue;
                }

                SerializedProperty magnitude = modifier.FindPropertyRelative("Magnitude");
                if (magnitude == null)
                {
                    continue;
                }

                SerializedProperty baseValue = magnitude.FindPropertyRelative("BaseValue");
                SerializedProperty perLevel = magnitude.FindPropertyRelative("ScalingFactorPerLevel");
                double value = System.Math.Abs(baseValue != null ? baseValue.floatValue : 0f);
                if (perLevel != null)
                {
                    value += System.Math.Abs(perLevel.floatValue) * (Core.GASRuntimeDataContract.MaxGameplayLevel - 1);
                }

                if (value > 1.0)
                {
                    combinedFactor *= value;
                }
            }

            if (combinedFactor <= 1.0)
            {
                return false;
            }

            // Compare in log space so a huge factor never renders as "Infinity" in the message.
            double logWorstCase = System.Math.Log(combinedFactor) * stackLimit;
            if (logWorstCase <= System.Math.Log(MaxRepresentable))
            {
                return false;
            }

            double safe = System.Math.Floor(System.Math.Log(MaxRepresentable) / System.Math.Log(combinedFactor));
            maxSafeStacks = safe < 1.0 ? 1 : (int)safe;
            return true;
        }

        private static void DrawWarningBox(string message)
        {
            EditorGUILayout.HelpBox(message, MessageType.Warning);
        }

        #endregion

        #region Derived Fields

        private void DrawDerivedClassFields()
        {
            if (derivedProperties.Count > 0)
            {
                EditorGUILayout.Space(8);
                DrawSectionLine();
                showDerivedFields = EditorGUILayout.Foldout(showDerivedFields, derivedFieldsLabel, true);

                if (showDerivedFields)
                {
                    EditorGUI.indentLevel++;
                    for (int i = 0; i < derivedProperties.Count; i++)
                    {
                        EditorGUILayout.PropertyField(derivedProperties[i], true);
                    }
                    EditorGUI.indentLevel--;
                }
            }
        }

        #endregion

        #region Helpers

        private bool IsDurationMagnitudeEnabled()
        {
            if (durationMagnitudeProp == null)
            {
                return false;
            }

            var enabledProp = durationMagnitudeProp.FindPropertyRelative("Enabled");
            return enabledProp != null && enabledProp.boolValue;
        }

        private string GetDurationMagnitudeTypeLabel()
        {
            var calculationTypeProp = durationMagnitudeProp?.FindPropertyRelative("CalculationType");
            if (calculationTypeProp == null)
            {
                return "?";
            }

            switch ((Runtime.EGameplayEffectMagnitudeCalculation)calculationTypeProp.enumValueIndex)
            {
                case Runtime.EGameplayEffectMagnitudeCalculation.ScalableFloat:
                    return "ScalableFloat";
                case Runtime.EGameplayEffectMagnitudeCalculation.AttributeBased:
                    return "AttributeBased";
                case Runtime.EGameplayEffectMagnitudeCalculation.SetByCaller:
                    return "SetByCaller";
                default:
                    return "?";
            }
        }

        private void UpdateModifiersFoldoutLabel(int modifierCount, int abilityCount)
        {
            if (modifierCount == modifiersFoldoutCount && abilityCount == grantedAbilitiesFoldoutCount)
            {
                return;
            }

            modifiersFoldoutCount = modifierCount;
            grantedAbilitiesFoldoutCount = abilityCount;
            modifiersFoldoutLabel = $"Modifiers & Execution  ({modifierCount} modifier{(modifierCount != 1 ? "s" : "")}, " +
                $"{abilityCount} granted abilit{(abilityCount != 1 ? "ies" : "y")})";
        }

        private static void DrawSectionLine()
        {
            EditorGUILayout.Space(2);
            Rect lineRect = EditorGUILayout.GetControlRect(false, 1);
            EditorGUI.DrawRect(lineRect, s_SectionLine);
            EditorGUILayout.Space(2);
        }

        #endregion
    }

    [CustomPropertyDrawer(typeof(Runtime.ModifierInfoSerializable))]
    public sealed class ModifierInfoSerializableDrawer : PropertyDrawer
    {
        private const float VERTICAL_SPACING = 2f;
        private static readonly GUIContent s_TargetAttribute = new GUIContent("Target Attribute");
        private static readonly GUIContent s_Operation = new GUIContent("Operation");
        private static readonly GUIContent s_EvaluationChannel = new GUIContent("Evaluation Channel");
        private static readonly GUIContent s_MagnitudeType = new GUIContent("Magnitude Type");
        private static readonly GUIContent s_BackingAttribute = new GUIContent("Backing Attribute");
        private static readonly GUIContent s_CaptureSource = new GUIContent("Capture Source");
        private static readonly GUIContent s_AttributeValue = new GUIContent("Attribute Value");
        private static readonly GUIContent s_CaptureTiming = new GUIContent("Capture Timing");
        private static readonly GUIContent s_Coefficient = new GUIContent("Coefficient");
        private static readonly GUIContent s_PreAdd = new GUIContent("Pre Add");
        private static readonly GUIContent s_PostAdd = new GUIContent("Post Add");
        private static readonly GUIContent s_DataTag = new GUIContent("Data Tag");
        private static readonly GUIContent s_DataName = new GUIContent("Data Name");
        private static readonly GUIContent s_DefaultValue = new GUIContent("Default Value");
        private static readonly GUIContent s_WarnIfMissing = new GUIContent("Warn If Missing");
        private static readonly GUIContent s_Magnitude = new GUIContent("Magnitude");
        private static readonly GUIContent s_CustomCalculation = new GUIContent("Calculation Asset");
        private static readonly GUIContent s_FallbackMagnitude = new GUIContent("Fallback Magnitude");

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            property.isExpanded = EditorGUI.Foldout(line, property.isExpanded, label, true);
            if (!property.isExpanded)
            {
                EditorGUI.EndProperty();
                return;
            }

            EditorGUI.indentLevel++;
            Advance(ref line);

            DrawRelativeProperty(ref line, property, "AttributeName", s_TargetAttribute);
            DrawRelativeProperty(ref line, property, "Operation", s_Operation);
            DrawRelativeProperty(ref line, property, "EvaluationChannel", s_EvaluationChannel);

            var magnitudeTypeProp = property.FindPropertyRelative("MagnitudeCalculationType");
            EditorGUI.PropertyField(line, magnitudeTypeProp, s_MagnitudeType);
            Advance(ref line);

            var magnitudeType = (Runtime.EGameplayEffectMagnitudeCalculation)magnitudeTypeProp.enumValueIndex;
            switch (magnitudeType)
            {
                case Runtime.EGameplayEffectMagnitudeCalculation.AttributeBased:
                    DrawRelativeProperty(ref line, property, "BackingAttributeName", s_BackingAttribute);
                    DrawRelativeProperty(ref line, property, "AttributeCaptureSource", s_CaptureSource);
                    DrawRelativeProperty(ref line, property, "AttributeCalculationType", s_AttributeValue);
                    DrawRelativeProperty(ref line, property, "AttributeSnapshotPolicy", s_CaptureTiming);
                    DrawRelativeProperty(ref line, property, "AttributeCoefficient", s_Coefficient, true);
                    DrawRelativeProperty(ref line, property, "AttributePreMultiplyAdditiveValue", s_PreAdd, true);
                    DrawRelativeProperty(ref line, property, "AttributePostMultiplyAdditiveValue", s_PostAdd, true);
                    break;
                case Runtime.EGameplayEffectMagnitudeCalculation.SetByCaller:
                    DrawRelativeProperty(ref line, property, "SetByCallerDataTag", s_DataTag);
                    DrawRelativeProperty(ref line, property, "SetByCallerDataName", s_DataName);
                    DrawRelativeProperty(ref line, property, "SetByCallerDefaultValue", s_DefaultValue);
                    DrawRelativeProperty(ref line, property, "WarnIfSetByCallerMissing", s_WarnIfMissing);
                    break;
                case Runtime.EGameplayEffectMagnitudeCalculation.CustomCalculation:
                    DrawRelativeProperty(ref line, property, "CustomCalculation", s_CustomCalculation);
                    if (property.FindPropertyRelative("CustomCalculation").objectReferenceValue == null)
                    {
                        line.height = EditorGUIUtility.singleLineHeight * 2.2f;
                        EditorGUI.HelpBox(
                            line,
                            "No calculation asset assigned; this modifier uses Fallback Magnitude.",
                            MessageType.Warning);
                        Advance(ref line);
                    }
                    DrawRelativeProperty(ref line, property, "Magnitude", s_FallbackMagnitude, true);
                    DrawRelativeProperty(ref line, property, "AttributeSnapshotPolicy", s_CaptureTiming);
                    break;
                default:
                    DrawRelativeProperty(ref line, property, "Magnitude", s_Magnitude, true);
                    break;
            }

            EditorGUI.indentLevel--;
            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight;
            if (property.isExpanded)
            {
                AddRelativePropertyHeight(ref height, property, "AttributeName");
                AddRelativePropertyHeight(ref height, property, "Operation");
                AddRelativePropertyHeight(ref height, property, "EvaluationChannel");
                AddRelativePropertyHeight(ref height, property, "MagnitudeCalculationType");

                var magnitudeTypeProp = property.FindPropertyRelative("MagnitudeCalculationType");
                var magnitudeType = (Runtime.EGameplayEffectMagnitudeCalculation)magnitudeTypeProp.enumValueIndex;
                switch (magnitudeType)
                {
                    case Runtime.EGameplayEffectMagnitudeCalculation.AttributeBased:
                        AddRelativePropertyHeight(ref height, property, "BackingAttributeName");
                        AddRelativePropertyHeight(ref height, property, "AttributeCaptureSource");
                        AddRelativePropertyHeight(ref height, property, "AttributeCalculationType");
                        AddRelativePropertyHeight(ref height, property, "AttributeSnapshotPolicy");
                        AddRelativePropertyHeight(ref height, property, "AttributeCoefficient", true);
                        AddRelativePropertyHeight(ref height, property, "AttributePreMultiplyAdditiveValue", true);
                        AddRelativePropertyHeight(ref height, property, "AttributePostMultiplyAdditiveValue", true);
                        break;
                    case Runtime.EGameplayEffectMagnitudeCalculation.SetByCaller:
                        AddRelativePropertyHeight(ref height, property, "SetByCallerDataTag");
                        AddRelativePropertyHeight(ref height, property, "SetByCallerDataName");
                        AddRelativePropertyHeight(ref height, property, "SetByCallerDefaultValue");
                        AddRelativePropertyHeight(ref height, property, "WarnIfSetByCallerMissing");
                        break;
                    case Runtime.EGameplayEffectMagnitudeCalculation.CustomCalculation:
                        AddRelativePropertyHeight(ref height, property, "CustomCalculation");
                        if (property.FindPropertyRelative("CustomCalculation").objectReferenceValue == null)
                        {
                            height += VERTICAL_SPACING + (EditorGUIUtility.singleLineHeight * 2.2f);
                        }
                        AddRelativePropertyHeight(ref height, property, "Magnitude", true);
                        AddRelativePropertyHeight(ref height, property, "AttributeSnapshotPolicy");
                        break;
                    default:
                        AddRelativePropertyHeight(ref height, property, "Magnitude", true);
                        break;
                }
            }

            return height;
        }

        private static void DrawRelativeProperty(
            ref Rect line,
            SerializedProperty property,
            string relativePropertyName,
            GUIContent label,
            bool includeChildren = false)
        {
            var relativeProperty = property.FindPropertyRelative(relativePropertyName);
            line.height = EditorGUI.GetPropertyHeight(relativeProperty, includeChildren);
            EditorGUI.PropertyField(line, relativeProperty, label, includeChildren);
            Advance(ref line);
        }

        private static void AddRelativePropertyHeight(
            ref float height,
            SerializedProperty property,
            string relativePropertyName,
            bool includeChildren = false)
        {
            var relativeProperty = property.FindPropertyRelative(relativePropertyName);
            height += VERTICAL_SPACING + EditorGUI.GetPropertyHeight(relativeProperty, includeChildren);
        }

        private static void Advance(ref Rect line)
        {
            line.y += line.height + VERTICAL_SPACING;
            line.height = EditorGUIUtility.singleLineHeight;
        }
    }

    /// <summary>
    /// Drawer for the data-driven duration source on effect assets: an enable toggle, the
    /// calculation type, and only the fields relevant to the selected type.
    /// </summary>
    [CustomPropertyDrawer(typeof(Runtime.DurationMagnitudeSerializable))]
    public sealed class DurationMagnitudeSerializableDrawer : PropertyDrawer
    {
        private const float VERTICAL_SPACING = 2f;
        private static readonly GUIContent s_Enabled = new GUIContent("Enabled");
        private static readonly GUIContent s_CalculationType = new GUIContent("Calculation Type");
        private static readonly GUIContent s_Magnitude = new GUIContent("Magnitude (sec)");
        private static readonly GUIContent s_BackingAttribute = new GUIContent("Backing Attribute");
        private static readonly GUIContent s_CaptureSource = new GUIContent("Capture Source");
        private static readonly GUIContent s_AttributeValue = new GUIContent("Attribute Value");
        private static readonly GUIContent s_CaptureTiming = new GUIContent("Capture Timing");
        private static readonly GUIContent s_Coefficient = new GUIContent("Coefficient");
        private static readonly GUIContent s_PreAdd = new GUIContent("Pre Add");
        private static readonly GUIContent s_PostAdd = new GUIContent("Post Add");
        private static readonly GUIContent s_DataTag = new GUIContent("Data Tag");
        private static readonly GUIContent s_DataName = new GUIContent("Data Name");
        private static readonly GUIContent s_DefaultValue = new GUIContent("Default Value (sec)");
        private static readonly GUIContent s_WarnIfMissing = new GUIContent("Warn If Missing");

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var line = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
            property.isExpanded = EditorGUI.Foldout(line, property.isExpanded, label, true);
            if (!property.isExpanded)
            {
                EditorGUI.EndProperty();
                return;
            }

            EditorGUI.indentLevel++;
            Advance(ref line);

            var enabledProp = property.FindPropertyRelative("Enabled");
            EditorGUI.PropertyField(line, enabledProp, s_Enabled);
            Advance(ref line);

            if (!enabledProp.boolValue)
            {
                EditorGUI.indentLevel--;
                EditorGUI.EndProperty();
                return;
            }

            var calculationTypeProp = property.FindPropertyRelative("CalculationType");
            EditorGUI.PropertyField(line, calculationTypeProp, s_CalculationType);
            Advance(ref line);

            switch ((Runtime.EGameplayEffectMagnitudeCalculation)calculationTypeProp.enumValueIndex)
            {
                case Runtime.EGameplayEffectMagnitudeCalculation.AttributeBased:
                    DrawRelativeProperty(ref line, property, "BackingAttributeName", s_BackingAttribute);
                    DrawRelativeProperty(ref line, property, "AttributeCaptureSource", s_CaptureSource);
                    DrawRelativeProperty(ref line, property, "AttributeCalculationType", s_AttributeValue);
                    DrawRelativeProperty(ref line, property, "AttributeSnapshotPolicy", s_CaptureTiming);
                    DrawRelativeProperty(ref line, property, "AttributeCoefficient", s_Coefficient, true);
                    DrawRelativeProperty(ref line, property, "AttributePreMultiplyAdditiveValue", s_PreAdd, true);
                    DrawRelativeProperty(ref line, property, "AttributePostMultiplyAdditiveValue", s_PostAdd, true);
                    break;
                case Runtime.EGameplayEffectMagnitudeCalculation.SetByCaller:
                    DrawRelativeProperty(ref line, property, "SetByCallerDataTag", s_DataTag);
                    DrawRelativeProperty(ref line, property, "SetByCallerDataName", s_DataName);
                    DrawRelativeProperty(ref line, property, "SetByCallerDefaultValue", s_DefaultValue);
                    DrawRelativeProperty(ref line, property, "WarnIfSetByCallerMissing", s_WarnIfMissing);
                    break;
                case Runtime.EGameplayEffectMagnitudeCalculation.CustomCalculation:
                    line.height = EditorGUIUtility.singleLineHeight * 2.2f;
                    EditorGUI.HelpBox(line, "CustomCalculation is not supported for durations.", MessageType.Warning);
                    Advance(ref line);
                    break;
                default:
                    DrawRelativeProperty(ref line, property, "Magnitude", s_Magnitude, true);
                    break;
            }

            EditorGUI.indentLevel--;
            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = EditorGUIUtility.singleLineHeight;
            if (!property.isExpanded)
            {
                return height;
            }

            var enabledProp = property.FindPropertyRelative("Enabled");
            AddRelativePropertyHeight(ref height, property, "Enabled");
            if (enabledProp == null || !enabledProp.boolValue)
            {
                return height;
            }

            AddRelativePropertyHeight(ref height, property, "CalculationType");
            var calculationTypeProp = property.FindPropertyRelative("CalculationType");
            switch ((Runtime.EGameplayEffectMagnitudeCalculation)calculationTypeProp.enumValueIndex)
            {
                case Runtime.EGameplayEffectMagnitudeCalculation.AttributeBased:
                    AddRelativePropertyHeight(ref height, property, "BackingAttributeName");
                    AddRelativePropertyHeight(ref height, property, "AttributeCaptureSource");
                    AddRelativePropertyHeight(ref height, property, "AttributeCalculationType");
                    AddRelativePropertyHeight(ref height, property, "AttributeSnapshotPolicy");
                    AddRelativePropertyHeight(ref height, property, "AttributeCoefficient", true);
                    AddRelativePropertyHeight(ref height, property, "AttributePreMultiplyAdditiveValue", true);
                    AddRelativePropertyHeight(ref height, property, "AttributePostMultiplyAdditiveValue", true);
                    break;
                case Runtime.EGameplayEffectMagnitudeCalculation.SetByCaller:
                    AddRelativePropertyHeight(ref height, property, "SetByCallerDataTag");
                    AddRelativePropertyHeight(ref height, property, "SetByCallerDataName");
                    AddRelativePropertyHeight(ref height, property, "SetByCallerDefaultValue");
                    AddRelativePropertyHeight(ref height, property, "WarnIfSetByCallerMissing");
                    break;
                case Runtime.EGameplayEffectMagnitudeCalculation.CustomCalculation:
                    height += VERTICAL_SPACING + (EditorGUIUtility.singleLineHeight * 2.2f);
                    break;
                default:
                    AddRelativePropertyHeight(ref height, property, "Magnitude", true);
                    break;
            }

            return height;
        }

        private static void DrawRelativeProperty(
            ref Rect line,
            SerializedProperty property,
            string relativePropertyName,
            GUIContent label,
            bool includeChildren = false)
        {
            var relativeProperty = property.FindPropertyRelative(relativePropertyName);
            line.height = EditorGUI.GetPropertyHeight(relativeProperty, includeChildren);
            EditorGUI.PropertyField(line, relativeProperty, label, includeChildren);
            Advance(ref line);
        }

        private static void AddRelativePropertyHeight(
            ref float height,
            SerializedProperty property,
            string relativePropertyName,
            bool includeChildren = false)
        {
            var relativeProperty = property.FindPropertyRelative(relativePropertyName);
            height += VERTICAL_SPACING + EditorGUI.GetPropertyHeight(relativeProperty, includeChildren);
        }

        private static void Advance(ref Rect line)
        {
            line.y += line.height + VERTICAL_SPACING;
            line.height = EditorGUIUtility.singleLineHeight;
        }
    }
}
