using System;
using UnityEngine;
using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayTags.Core;

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
        public GameplayTag SetByCallerDataTag;

        [Tooltip("SetByCaller name key. This is intended for local/legacy code paths; GameplayTag is preferred for networking.")]
        public string SetByCallerDataName;

        [Tooltip("Fallback value when SetByCaller data is missing.")]
        public float SetByCallerDefaultValue;

        [Tooltip("Logs a warning when SetByCaller data is missing.")]
        public bool WarnIfSetByCallerMissing = true;
    }
}
