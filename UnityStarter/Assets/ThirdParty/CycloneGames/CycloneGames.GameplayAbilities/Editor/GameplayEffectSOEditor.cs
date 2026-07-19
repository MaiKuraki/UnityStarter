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
        private static readonly GUIContent s_NameContent = new GUIContent("Name");
        private static readonly GUIContent s_DurationPolicyContent = new GUIContent("Duration Policy");
        private static readonly GUIContent s_DurationContent = new GUIContent("Duration (sec)");
        private static readonly GUIContent s_PeriodContent = new GUIContent("Period (sec)");
        private static readonly GUIContent s_StackingContent = new GUIContent("Stacking");
        private static readonly GUIContent s_ModifiersContent = new GUIContent("Attribute Modifiers");
        private static readonly GUIContent s_ExecutionContent = new GUIContent("Custom Execution");
        private static readonly GUIContent s_GrantedAbilitiesContent = new GUIContent("Granted Abilities");
        private static readonly GUIContent s_AssetTagsContent = new GUIContent("Asset Tags (describe effect)");
        private static readonly GUIContent s_GrantedTagsContent = new GUIContent("Granted Tags (applied to target)");
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
            "Executes the first periodic tick immediately on application.");
        private static readonly GUIContent s_OverflowEffectsContent = new GUIContent("Overflow Effects");
        private static readonly GUIContent s_DenyOverflowContent = new GUIContent(
            "Deny Overflow Application",
            "Prevents the attempted application when the stack is already at its limit.");

        private readonly StringBuilder sb = new StringBuilder(256);
        private readonly List<SerializedProperty> derivedProperties = new List<SerializedProperty>(8);
        private SerializedProperty stackingTypeProp;
        private SerializedProperty stackingLimitProp;
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
            stackingTypeProp = stackingProp?.FindPropertyRelative("Type");
            stackingLimitProp = stackingProp?.FindPropertyRelative("Limit");
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
                }

                EditorGUILayout.PropertyField(periodProp, s_PeriodContent);
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

                EditorGUILayout.PropertyField(executionProp, s_ExecutionContent);
                EditorGUILayout.PropertyField(grantedAbilitiesProp, s_GrantedAbilitiesContent, true);
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
                EditorGUILayout.PropertyField(assetTagsProp, s_AssetTagsContent);
                EditorGUILayout.PropertyField(grantedTagsProp, s_GrantedTagsContent);
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
            if (stackingTypeProp != null && stackingTypeProp.enumValueIndex != 0)
            {
                string stackingLabel = ((Runtime.EGameplayEffectStackingType)stackingTypeProp.enumValueIndex) switch
                {
                    Runtime.EGameplayEffectStackingType.AggregateBySource => "AggregateBySource",
                    Runtime.EGameplayEffectStackingType.AggregateByTarget => "AggregateByTarget",
                    _ => "?"
                };
                sb.Append("  \u2022  Stacking: ").Append(stackingLabel);
                if (stackingLimitProp != null) sb.Append(" (max ").Append(stackingLimitProp.intValue).Append(')');
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
            if (policy == Runtime.EDurationPolicy.Instant && stackingTypeProp != null && stackingTypeProp.enumValueIndex != 0)
            {
                DrawWarningBox("Stacking is configured but Duration Policy is 'Instant'. Instant effects cannot stack.");
                hasWarnings = true;
            }

            // Stacking limit of 0 or negative
            if (stackingTypeProp != null && stackingTypeProp.enumValueIndex != 0)
            {
                if (stackingLimitProp != null && stackingLimitProp.intValue <= 0)
                {
                    DrawWarningBox("Stacking limit is \u2264 0. This may cause unexpected behavior.");
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
                    line.height = EditorGUIUtility.singleLineHeight * 2.2f;
                    EditorGUI.HelpBox(
                        line,
                        "ScriptableObject modifiers cannot serialize custom calculation instances. Use ScalableFloat, AttributeBased, SetByCaller, or C# runtime construction.",
                        MessageType.Warning);
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
                        height += VERTICAL_SPACING + (EditorGUIUtility.singleLineHeight * 2.2f);
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
}
