using System;
using UnityEngine;
using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayTags.Core;
using CycloneGames.GameplayTags.Unity.Runtime;

namespace CycloneGames.GameplayAbilities.Runtime
{
    public sealed class AttributeNameSelectorAttribute : PropertyAttribute
    {
        public AttributeNameSelectorAttribute()
        {
        }

        public AttributeNameSelectorAttribute(Type constantsType)
        {
            ConstantsType = constantsType;
        }

        public Type ConstantsType { get; }
    }

    [System.Serializable]
    public class ModifierInfoSerializable
    {
        [Tooltip("The name of the attribute to modify. Must match the name in the AttributeSet exactly.")]
        [AttributeNameSelector]
        public string AttributeName;

        [Tooltip("The operation to perform on the attribute.")]
        public EAttributeModifierOperation Operation;

        [Tooltip("Ordered evaluation channel for this modifier. Channel0 is the default general-purpose path.")]
        public GASModifierEvaluationChannel EvaluationChannel;

        [Tooltip("How this modifier magnitude is calculated.")]
        public EGameplayEffectMagnitudeCalculation MagnitudeCalculationType;

        [Tooltip("The magnitude of the modification. Can be a fixed value or scale with level.")]
        public ScalableFloat Magnitude;

        [Tooltip("The source or target attribute used when MagnitudeCalculationType is AttributeBased.")]
        [AttributeNameSelector]
        public string BackingAttributeName;

        [Tooltip("Whether the backing attribute is read from the effect source or target.")]
        public EGameplayEffectAttributeCaptureSource AttributeCaptureSource;

        [Tooltip("Which value from the backing attribute is used.")]
        public EAttributeBasedFloatCalculationType AttributeCalculationType;

        [Tooltip("Whether the backing attribute magnitude is frozen at application time or evaluated live.")]
        public EGameplayEffectAttributeCaptureSnapshot AttributeSnapshotPolicy;

        [Tooltip("Coefficient in Coefficient * (AttributeValue + PreMultiplyAdditiveValue) + PostMultiplyAdditiveValue.")]
        public ScalableFloat AttributeCoefficient = new ScalableFloat(1f);

        [Tooltip("Additive value applied before multiplication.")]
        public ScalableFloat AttributePreMultiplyAdditiveValue;

        [Tooltip("Additive value applied after multiplication.")]
        public ScalableFloat AttributePostMultiplyAdditiveValue;

        [Tooltip("SetByCaller GameplayTag key. Prefer this for replicated effects.")]
        public SerializableGameplayTag SetByCallerDataTag;

        [Tooltip("SetByCaller name key for non-replicated local workflows. Use GameplayTag keys for networked effects.")]
        public string SetByCallerDataName;

        [Tooltip("Fallback value when SetByCaller data is missing.")]
        public float SetByCallerDefaultValue;

        [Tooltip("Logs a warning when SetByCaller data is missing.")]
        public bool WarnIfSetByCallerMissing = true;
    }

    /// <summary>
    /// Authoring mirror of <see cref="DurationMagnitudeInfo"/> for ScriptableObject effect assets.
    /// When <see cref="Enabled"/> is false the runtime definition keeps the static Duration value,
    /// so existing assets are unaffected.
    /// </summary>
    [System.Serializable]
    public class DurationMagnitudeSerializable
    {
        [Tooltip("When enabled, each spec's duration is evaluated from this magnitude instead of the static Duration value.")]
        public bool Enabled;

        [Tooltip("How the duration magnitude is calculated. CustomCalculation is not supported for durations.")]
        public EGameplayEffectMagnitudeCalculation CalculationType = EGameplayEffectMagnitudeCalculation.ScalableFloat;

        [Tooltip("Constant duration, optionally scaled by level.")]
        public ScalableFloat Magnitude = new ScalableFloat(1f);

        [Tooltip("The attribute read when CalculationType is AttributeBased (e.g. a CooldownDuration stat).")]
        [AttributeNameSelector]
        public string BackingAttributeName;

        [Tooltip("Whether the backing attribute is read from the effect source or target.")]
        public EGameplayEffectAttributeCaptureSource AttributeCaptureSource;

        [Tooltip("Which value from the backing attribute is used.")]
        public EAttributeBasedFloatCalculationType AttributeCalculationType;

        [Tooltip("Whether the backing attribute magnitude is frozen at application time or evaluated live.")]
        public EGameplayEffectAttributeCaptureSnapshot AttributeSnapshotPolicy;

        [Tooltip("Coefficient in Coefficient * (AttributeValue + PreMultiplyAdditiveValue) + PostMultiplyAdditiveValue.")]
        public ScalableFloat AttributeCoefficient = new ScalableFloat(1f);

        [Tooltip("Additive value applied before multiplication.")]
        public ScalableFloat AttributePreMultiplyAdditiveValue;

        [Tooltip("Additive value applied after multiplication.")]
        public ScalableFloat AttributePostMultiplyAdditiveValue;

        [Tooltip("SetByCaller GameplayTag key. Prefer this for replicated effects.")]
        public SerializableGameplayTag SetByCallerDataTag;

        [Tooltip("SetByCaller name key for non-replicated local workflows. Use GameplayTag keys for networked effects.")]
        public string SetByCallerDataName;

        [Tooltip("Fallback duration when SetByCaller data is missing.")]
        public float SetByCallerDefaultValue;

        [Tooltip("Logs a warning when SetByCaller data is missing.")]
        public bool WarnIfSetByCallerMissing = true;

        /// <summary>
        /// Converts the authoring data to the immutable runtime definition data.
        /// Returns null when disabled or when CustomCalculation is selected (unsupported for durations).
        /// </summary>
        public DurationMagnitudeInfo ToRuntimeInfo()
        {
            if (!Enabled)
            {
                return null;
            }

            switch (CalculationType)
            {
                case EGameplayEffectMagnitudeCalculation.AttributeBased:
                    return new DurationMagnitudeInfo(new AttributeBasedMagnitude(
                        BackingAttributeName,
                        AttributeCaptureSource,
                        AttributeCalculationType,
                        AttributeCoefficient,
                        AttributePreMultiplyAdditiveValue,
                        AttributePostMultiplyAdditiveValue,
                        AttributeSnapshotPolicy));
                case EGameplayEffectMagnitudeCalculation.SetByCaller:
                    return new DurationMagnitudeInfo(
                        !string.IsNullOrEmpty(SetByCallerDataTag.TagName)
                            ? new SetByCallerMagnitude(SetByCallerDataTag.Tag, SetByCallerDefaultValue, WarnIfSetByCallerMissing)
                            : new SetByCallerMagnitude(SetByCallerDataName, SetByCallerDefaultValue, WarnIfSetByCallerMissing));
                case EGameplayEffectMagnitudeCalculation.ScalableFloat:
                    return new DurationMagnitudeInfo(Magnitude);
                default:
                    return null;
            }
        }
    }
}
