using System.Globalization;

using UnityEditor;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Editor
{
    /// <summary>
    /// Custom property drawer for GameplayEffectStacking struct.
    /// Rows that cannot affect the current configuration are hidden rather than greyed out, so the
    /// panel only shows what the author can actually act on.
    /// </summary>
    [CustomPropertyDrawer(typeof(Runtime.GameplayEffectStacking))]
    public class GameplayEffectStackingDrawer : PropertyDrawer
    {
        private static readonly GUIContent s_TypeContent = new GUIContent("Stacking Type");
        private static readonly GUIContent s_LimitModeContent = new GUIContent(
            "Stack Limit Mode",
            "Limited: the stack count stops at Stack Limit.\n\n" +
            "Unlimited: no gameplay cap. Resolves to 65535 because the Core mirror stores the stack " +
            "count as a ushort, so the value saturates instead of wrapping.");
        private static readonly GUIContent s_LimitContent = new GUIContent(
            "Stack Limit",
            "Maximum number of stacks, clamped to the supported range on commit.\n" +
            "Values above 1 change how Multiply/Division magnitudes scale.");
        private static readonly GUIContent s_MagnitudePolicyContent = new GUIContent(
            "Magnitude Policy",
            "How the stack count scales this effect's Multiply/Division magnitudes.\n\n" +
            "LinearBias (default): (M - 1) x stack + 1. Every stack adds the same delta, so a x1.1 buff " +
            "at 5 stacks is x1.5. Matches Unreal Engine and the prevalent ARPG/MMORPG convention.\n\n" +
            "Exponential: M ^ stack. Every stack compounds, so a x1.1 buff at 5 stacks is x1.61. " +
            "Use for deliberate 'double per stack' designs; the authoring range check rejects limits " +
            "whose worst-case product leaves the Q32.32 range.");
        private static readonly GUIContent s_MagnitudePolicyUnusedContent = new GUIContent(
            "Magnitude Policy (ignored)",
            "This effect has no Multiply or Division modifier, but a non-default policy is still stored. " +
            "It is shown disabled so the dormant value stays visible; adding a multiplicative modifier applies it again.");
        private static readonly GUIContent s_DurationContent = new GUIContent("Duration Refresh");
        private static readonly GUIContent s_ExpirationContent = new GUIContent("Expiration Policy");
        private static readonly GUIContent s_PeriodResetContent = new GUIContent("Period Reset");

        private static readonly GUIContent s_LimitUnusedContent = new GUIContent(
            "Stack Limit (ignored)",
            "Stack Limit Mode is Unlimited, but this effect still carries a stack limit value. " +
            "It is shown disabled so the dormant value stays visible; switching back to Limited applies it again.");

        /// <summary>How a row is presented for the current configuration.</summary>
        private enum RowState
        {
            /// <summary>Cannot affect anything and holds its default: nothing worth showing.</summary>
            Hidden,

            /// <summary>Cannot affect anything but holds a non-default value: show it, locked.</summary>
            Disabled,

            /// <summary>Active.</summary>
            Enabled
        }

        /// <summary>Which optional rows are meaningful for the current configuration.</summary>
        private readonly struct RowPlan
        {
            public readonly RowState Limit;
            public readonly RowState MagnitudePolicy;
            public readonly RowState PeriodReset;
            public readonly int Count;

            public RowPlan(RowState limit, RowState magnitudePolicy, RowState periodReset)
            {
                Limit = limit;
                MagnitudePolicy = magnitudePolicy;
                PeriodReset = periodReset;

                // Type, LimitMode, DurationRefresh and ExpirationPolicy are always shown while stacking.
                Count = 4 +
                        (limit != RowState.Hidden ? 1 : 0) +
                        (magnitudePolicy != RowState.Hidden ? 1 : 0) +
                        (periodReset != RowState.Hidden ? 1 : 0);
            }
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var typeProp = property.FindPropertyRelative("Type");
            var stackingType = (Runtime.EGameplayEffectStackingType)typeProp.enumValueIndex;

            if (stackingType == Runtime.EGameplayEffectStackingType.None)
            {
                return EditorGUIUtility.singleLineHeight;
            }

            RowPlan plan = BuildRowPlan(property);
            return EditorGUIUtility.singleLineHeight * plan.Count +
                   EditorGUIUtility.standardVerticalSpacing * (plan.Count - 1);
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var typeProp = property.FindPropertyRelative("Type");
            var limitModeProp = property.FindPropertyRelative("LimitMode");
            var limitProp = property.FindPropertyRelative("Limit");
            var magnitudePolicyProp = property.FindPropertyRelative("MagnitudePolicy");
            var durationPolicyProp = property.FindPropertyRelative("DurationPolicy");
            var expirationPolicyProp = property.FindPropertyRelative("ExpirationPolicy");
            var periodResetPolicyProp = property.FindPropertyRelative("PeriodResetPolicy");

            float lineHeight = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;
            int row = 0;

            Rect NextRect() => new Rect(position.x, position.y + (lineHeight + spacing) * row++, position.width, lineHeight);

            EditorGUI.PropertyField(NextRect(), typeProp, s_TypeContent);

            if ((Runtime.EGameplayEffectStackingType)typeProp.enumValueIndex == Runtime.EGameplayEffectStackingType.None)
            {
                EditorGUI.EndProperty();
                return;
            }

            RowPlan plan = BuildRowPlan(property);

            EditorGUI.indentLevel++;

            if (limitModeProp != null)
            {
                EditorGUI.PropertyField(NextRect(), limitModeProp, s_LimitModeContent);
            }

            if (plan.Limit != RowState.Hidden)
            {
                using (new EditorGUI.DisabledScope(plan.Limit == RowState.Disabled))
                {
                    DrawStackLimitField(NextRect(), limitProp, plan.Limit == RowState.Disabled);
                }
            }

            if (plan.MagnitudePolicy != RowState.Hidden && magnitudePolicyProp != null)
            {
                bool disabled = plan.MagnitudePolicy == RowState.Disabled;
                using (new EditorGUI.DisabledScope(disabled))
                {
                    EditorGUI.PropertyField(
                        NextRect(),
                        magnitudePolicyProp,
                        disabled ? s_MagnitudePolicyUnusedContent : s_MagnitudePolicyContent);
                }
            }

            EditorGUI.PropertyField(NextRect(), durationPolicyProp, s_DurationContent);
            EditorGUI.PropertyField(NextRect(), expirationPolicyProp, s_ExpirationContent);

            if (plan.PeriodReset != RowState.Hidden)
            {
                using (new EditorGUI.DisabledScope(plan.PeriodReset == RowState.Disabled))
                {
                    EditorGUI.PropertyField(NextRect(), periodResetPolicyProp, s_PeriodResetContent);
                }
            }

            EditorGUI.indentLevel--;

            EditorGUI.EndProperty();
        }

        /// <summary>
        /// A row is hidden only when it cannot affect the current configuration <b>and</b> still holds
        /// its default value. A non-default value stays visible so a validation error caused by a stale
        /// setting can always be found and fixed.
        /// </summary>
        private static RowPlan BuildRowPlan(SerializedProperty property)
        {
            var limitModeProp = property.FindPropertyRelative("LimitMode");
            var limitProp = property.FindPropertyRelative("Limit");

            bool unlimited = limitModeProp != null &&
                (Runtime.EGameplayEffectStackLimitMode)limitModeProp.enumValueIndex ==
                Runtime.EGameplayEffectStackLimitMode.Unlimited;

            RowState limitState;
            if (!unlimited)
            {
                limitState = RowState.Enabled;
            }
            else
            {
                // Unlimited ignores the value, but a configured limit must stay visible: switching back
                // to Limited would silently reapply a number the author can no longer see.
                limitState = limitProp != null && limitProp.intValue > 1 ? RowState.Disabled : RowState.Hidden;
            }

            var magnitudePolicyProp = property.FindPropertyRelative("MagnitudePolicy");
            RowState policyState;
            if (MagnitudePolicyApplies(property))
            {
                policyState = RowState.Enabled;
            }
            else
            {
                policyState = magnitudePolicyProp != null &&
                    magnitudePolicyProp.enumValueIndex != (int)Core.GASStackMagnitudePolicy.LinearBias
                        ? RowState.Disabled
                        : RowState.Hidden;
            }

            RowState periodResetState = RowState.Enabled;
            SerializedProperty period = property.serializedObject.FindProperty("Period");
            if (period != null && period.floatValue <= 0f)
            {
                SerializedProperty periodReset = property.FindPropertyRelative("PeriodResetPolicy");
                periodResetState = periodReset != null && periodReset.enumValueIndex != 0
                    ? RowState.Disabled
                    : RowState.Hidden;
            }

            return new RowPlan(limitState, policyState, periodResetState);
        }

        /// <summary>
        /// Uses the built-in delayed field instead of marshalling a text buffer by hand. A delayed
        /// field only reports a value when the author commits (Enter or focus loss), so nothing is
        /// written per keystroke: no SerializedObject rebuild mid-GUI, no control-id recycling, no
        /// focus jumping and no per-event repaint on long input.
        /// </summary>
        private static void DrawStackLimitField(Rect rect, SerializedProperty limitProp, bool ignoredByMode)
        {
            string text = limitProp.intValue.ToString(CultureInfo.InvariantCulture);

            EditorGUI.BeginChangeCheck();
            string committed = EditorGUI.DelayedTextField(
                rect,
                ignoredByMode ? s_LimitUnusedContent : s_LimitContent,
                text);
            if (!EditorGUI.EndChangeCheck())
            {
                return;
            }

            if (TryResolveStackLimit(committed, Runtime.GameplayEffect.MaxStackCount, out int value) &&
                value != limitProp.intValue)
            {
                // Flushed by the owning inspector's ApplyModifiedProperties at the end of its OnGUI.
                limitProp.intValue = value;
            }
        }

        /// <summary>
        /// Strict integer parse for a committed edit. Rejects signs, decimal separators, spaces and
        /// anything else the author may have typed, and clamps the accepted value into the supported
        /// range so the asset can always be built. Returns false when the text is not a number, in
        /// which case the field keeps its previous value.
        /// </summary>
        public static bool TryResolveStackLimit(string text, int maxLimit, out int value)
        {
            value = 0;
            if (string.IsNullOrEmpty(text) ||
                !long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long parsed))
            {
                return false;
            }

            if (parsed <= 0)
            {
                return false;
            }

            value = parsed > maxLimit ? maxLimit : (int)parsed;
            return true;
        }

        /// <summary>
        /// True when the owning effect has at least one Multiply or Division modifier. Additive
        /// modifiers always scale linearly (magnitude * stack) and Override ignores the stack count,
        /// so the policy has no observable effect without a multiplicative modifier.
        /// Unknown property layouts return true so the row is never hidden by mistake.
        /// </summary>
        public static bool MagnitudePolicyApplies(SerializedProperty stackingProperty)
        {
            SerializedProperty modifiers = stackingProperty.serializedObject.FindProperty("SerializableModifiers");
            if (modifiers == null || !modifiers.isArray)
            {
                return true;
            }

            for (int i = 0; i < modifiers.arraySize; i++)
            {
                SerializedProperty operation = modifiers.GetArrayElementAtIndex(i).FindPropertyRelative("Operation");
                if (operation == null)
                {
                    continue;
                }

                var value = (Runtime.EAttributeModifierOperation)operation.enumValueIndex;
                if (value == Runtime.EAttributeModifierOperation.Multiply ||
                    value == Runtime.EAttributeModifierOperation.Division)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
