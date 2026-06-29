using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayTags.Core;

namespace CycloneGames.GameplayAbilities.Runtime
{
    public static class GameplayEffectConstants
    {
        public const float INFINITE_DURATION = -1.0f;
    }

    /// <summary>
    /// Determines how an ability is automatically triggered.
    /// UE5: EGameplayAbilityTriggerSource::Type.
    /// </summary>
    public enum EAbilityTriggerSource
    {
        /// <summary>
        /// Triggered when a gameplay event with the matching tag is received (SendGameplayEventToActor).
        /// </summary>
        GameplayEvent,
        /// <summary>
        /// Triggered when the associated tag is added to the owner.
        /// </summary>
        OwnedTagAdded,
        /// <summary>
        /// Triggered when the associated tag is removed from the owner.
        /// </summary>
        OwnedTagRemoved
    }

    /// <summary>
    /// Defines how an ability can be automatically triggered.
    /// UE5: FAbilityTriggerData.
    /// </summary>
    [System.Serializable]
    public struct AbilityTriggerData
    {
        /// <summary>
        /// The tag that triggers this ability.
        /// </summary>
        public GameplayTag TriggerTag;

        /// <summary>
        /// How the trigger fires (on event, on tag added, on tag removed).
        /// </summary>
        public EAbilityTriggerSource TriggerSource;

        public AbilityTriggerData(GameplayTag triggerTag, EAbilityTriggerSource triggerSource)
        {
            TriggerTag = triggerTag;
            TriggerSource = triggerSource;
        }
    }

    /// <summary>
    /// Determines how a GameplayEffect's duration is handled.
    /// </summary>
    public enum EDurationPolicy
    {
        Instant,      // The effect is applied and removed immediately.
        HasDuration,  // The effect lasts for a specified duration.
        Infinite      // The effect lasts until explicitly removed.
    }

    /// <summary>
    /// The type of operation a modifier performs on an attribute.
    /// </summary>
    public enum EAttributeModifierOperation
    {
        Add,
        Multiply,
        Division,
        Override
    }

    /// <summary>
    /// Defines how the duration of a stackable effect is handled when a new stack is applied.
    /// </summary>
    public enum EGameplayEffectStackingDurationPolicy
    {
        RefreshOnSuccessfulApplication, // Resets the duration to its full value.
        NeverRefresh                    // The original duration is maintained.
    }

    /// <summary>
    /// Defines what happens when a stacked effect's duration expires.
    /// UE5: EGameplayEffectStackingExpirationPolicy.
    /// </summary>
    public enum EGameplayEffectStackingExpirationPolicy
    {
        /// <summary>
        /// The entire effect (all stacks) is removed when the duration expires.
        /// </summary>
        ClearEntireStack,
        /// <summary>
        /// Only one stack is removed when the duration expires. Duration is refreshed and the cycle repeats.
        /// </summary>
        RemoveSingleStackAndRefreshDuration,
        /// <summary>
        /// Duration is refreshed when a single stack is removed upon expiration (same as above but more explicit).
        /// </summary>
        RefreshDuration
    }

    /// <summary>
    /// Determines how an attribute value is captured for effect calculations.
    /// UE5: EGameplayEffectAttributeCaptureSource.
    /// </summary>
    public enum EGameplayEffectAttributeCaptureSource
    {
        /// <summary>
        /// Attribute is read from the source (instigator) of the effect.
        /// </summary>
        Source,
        /// <summary>
        /// Attribute is read from the target of the effect.
        /// </summary>
        Target
    }

    /// <summary>
    /// Determines when an attribute value is captured (snapshotted).
    /// UE5: bSnapshot on FGameplayEffectAttributeCaptureDefinition.
    /// </summary>
    public enum EGameplayEffectAttributeCaptureSnapshot
    {
        /// <summary>
        /// The attribute value is captured (frozen) at the time of effect application.
        /// Later changes to the source attribute will NOT affect this modifier.
        /// </summary>
        Snapshot,
        /// <summary>
        /// The attribute value is read live from the source each time the modifier is evaluated.
        /// </summary>
        NotSnapshot
    }

    /// <summary>
    /// Defines how a modifier magnitude is calculated.
    /// UE5: EGameplayEffectMagnitudeCalculation.
    /// </summary>
    public enum EGameplayEffectMagnitudeCalculation
    {
        ScalableFloat,
        AttributeBased,
        CustomCalculation,
        SetByCaller
    }

    /// <summary>
    /// Defines which part of a captured attribute contributes to an attribute-based magnitude.
    /// UE5: EAttributeBasedFloatCalculationType.
    /// </summary>
    public enum EAttributeBasedFloatCalculationType
    {
        AttributeMagnitude,
        AttributeBaseValue,
        AttributeBonusMagnitude
    }

    /// <summary>
    /// Defines how an effect stacks with other instances of the same effect.
    /// </summary>
    public enum EGameplayEffectStackingType
    {
        None,             // No stacking. A new, independent instance is always created.
        AggregateBySource,// Stacks are aggregated for each unique source.
        AggregateByTarget // Stacks are aggregated on the target, regardless of the source.
    }

    /// <summary>
    /// A complete definition of an effect's stacking behavior.
    /// </summary>
    [System.Serializable]
    public struct GameplayEffectStacking
    {
        public EGameplayEffectStackingType Type;
        public int Limit;
        public EGameplayEffectStackingDurationPolicy DurationPolicy;
        public EGameplayEffectStackingExpirationPolicy ExpirationPolicy;

        public GameplayEffectStacking(EGameplayEffectStackingType type, int limit, EGameplayEffectStackingDurationPolicy durationPolicy,
            EGameplayEffectStackingExpirationPolicy expirationPolicy = EGameplayEffectStackingExpirationPolicy.ClearEntireStack)
        {
            Type = type;
            Limit = limit;
            DurationPolicy = durationPolicy;
            ExpirationPolicy = expirationPolicy;
        }
    }

    /// <summary>
    /// Represents a float value that can scale with a level.
    /// </summary>
    [System.Serializable]
    public struct ScalableFloat
    {
        //  The base value of this float.
        public float BaseValue;
        //  A scaling factor applied per level. Formula: BaseValue + (ScalingFactorPerLevel * (Level - 1))
        public float ScalingFactorPerLevel;

        public ScalableFloat(float baseValue, float scalingFactorPerLevel = 0f)
        {
            BaseValue = baseValue;
            ScalingFactorPerLevel = scalingFactorPerLevel;
        }

        public float GetValueAtLevel(int level)
        {
            // Level 1 should use the base value, so we subtract 1.
            return BaseValue + (ScalingFactorPerLevel * (level > 0 ? level - 1 : 0));
        }

        public static implicit operator ScalableFloat(float value)
        {
            return new ScalableFloat(value);
        }
    }

    /// <summary>
    /// Base class for custom magnitude calculations that can read from the GameplayEffectSpec.
    /// This allows for dynamic, context-aware calculations (e.g., based on target's attributes).
    /// </summary>
    public abstract class GameplayModMagnitudeCalculation
    {
        /// <summary>
        /// Calculates the magnitude for a modifier.
        /// </summary>
        /// <param name="spec">The GameplayEffectSpec that is being applied.</param>
        /// <returns>The calculated magnitude.</returns>
        public abstract float CalculateMagnitude(GameplayEffectSpec spec);
    }

    /// <summary>
    /// Calculates a modifier magnitude from a captured source or target attribute.
    /// Formula matches the UE GAS attribute-based float core:
    /// Coefficient * (AttributeValue + PreMultiplyAdditiveValue) + PostMultiplyAdditiveValue.
    /// </summary>
    [System.Serializable]
    public readonly struct AttributeBasedMagnitude
    {
        public readonly string AttributeName;
        public readonly EGameplayEffectAttributeCaptureSource CaptureSource;
        public readonly EAttributeBasedFloatCalculationType CalculationType;
        public readonly ScalableFloat Coefficient;
        public readonly ScalableFloat PreMultiplyAdditiveValue;
        public readonly ScalableFloat PostMultiplyAdditiveValue;
        public readonly EGameplayEffectAttributeCaptureSnapshot SnapshotPolicy;

        public AttributeBasedMagnitude(
            string attributeName,
            EGameplayEffectAttributeCaptureSource captureSource = EGameplayEffectAttributeCaptureSource.Source,
            EAttributeBasedFloatCalculationType calculationType = EAttributeBasedFloatCalculationType.AttributeMagnitude,
            EGameplayEffectAttributeCaptureSnapshot snapshotPolicy = EGameplayEffectAttributeCaptureSnapshot.Snapshot)
            : this(
                attributeName,
                captureSource,
                calculationType,
                new ScalableFloat(1f),
                new ScalableFloat(0f),
                new ScalableFloat(0f),
                snapshotPolicy)
        {
        }

        public AttributeBasedMagnitude(
            string attributeName,
            EGameplayEffectAttributeCaptureSource captureSource,
            EAttributeBasedFloatCalculationType calculationType,
            ScalableFloat coefficient,
            ScalableFloat preMultiplyAdditiveValue,
            ScalableFloat postMultiplyAdditiveValue,
            EGameplayEffectAttributeCaptureSnapshot snapshotPolicy = EGameplayEffectAttributeCaptureSnapshot.Snapshot)
        {
            AttributeName = attributeName;
            CaptureSource = captureSource;
            CalculationType = calculationType;
            Coefficient = coefficient;
            PreMultiplyAdditiveValue = preMultiplyAdditiveValue;
            PostMultiplyAdditiveValue = postMultiplyAdditiveValue;
            SnapshotPolicy = snapshotPolicy;
        }

        public bool TryGetAttribute(GameplayEffectSpec spec, out GameplayAttribute attribute)
        {
            attribute = null;
            if (spec == null || string.IsNullOrEmpty(AttributeName))
            {
                return false;
            }

            var asc = CaptureSource == EGameplayEffectAttributeCaptureSource.Source ? spec.Source : spec.Target;
            if (asc == null)
            {
                return false;
            }

            attribute = asc.GetAttribute(AttributeName);
            return attribute != null;
        }

        public bool DependsOnAttribute(GameplayAttribute attribute, GameplayEffectSpec spec)
        {
            if (attribute == null)
            {
                return false;
            }

            return TryGetAttribute(spec, out var capturedAttribute) && ReferenceEquals(attribute, capturedAttribute);
        }

        public long CalculateMagnitudeRaw(GameplayEffectSpec spec, int level)
        {
            if (!TryGetAttribute(spec, out var attribute))
            {
                return 0L;
            }

            GASFixedValue attributeValue;
            switch (CalculationType)
            {
                case EAttributeBasedFloatCalculationType.AttributeBaseValue:
                    attributeValue = attribute.BaseFixedValue;
                    break;
                case EAttributeBasedFloatCalculationType.AttributeBonusMagnitude:
                    attributeValue = attribute.CurrentFixedValue - attribute.BaseFixedValue;
                    break;
                default:
                    attributeValue = attribute.CurrentFixedValue;
                    break;
            }

            var coefficient = GASFixedValue.FromFloat(Coefficient.GetValueAtLevel(level));
            var preAdd = GASFixedValue.FromFloat(PreMultiplyAdditiveValue.GetValueAtLevel(level));
            var postAdd = GASFixedValue.FromFloat(PostMultiplyAdditiveValue.GetValueAtLevel(level));
            return ((coefficient * (attributeValue + preAdd)) + postAdd).RawValue;
        }
    }

    /// <summary>
    /// Resolves a modifier magnitude from GameplayEffectSpec SetByCaller data.
    /// Use GameplayTag keys for replicated effects; name keys are local/legacy convenience keys.
    /// </summary>
    [System.Serializable]
    public readonly struct SetByCallerMagnitude
    {
        public readonly GameplayTag DataTag;
        public readonly string DataName;
        public readonly float DefaultValue;
        public readonly bool WarnIfNotFound;

        public SetByCallerMagnitude(GameplayTag dataTag, float defaultValue = 0f, bool warnIfNotFound = true)
        {
            DataTag = dataTag;
            DataName = null;
            DefaultValue = defaultValue;
            WarnIfNotFound = warnIfNotFound;
        }

        public SetByCallerMagnitude(string dataName, float defaultValue = 0f, bool warnIfNotFound = true)
        {
            DataTag = GameplayTag.None;
            DataName = dataName;
            DefaultValue = defaultValue;
            WarnIfNotFound = warnIfNotFound;
        }

        public long CalculateMagnitudeRaw(GameplayEffectSpec spec)
        {
            if (spec == null)
            {
                return GASFixedValue.FromFloat(DefaultValue).RawValue;
            }

            long defaultValueRaw = GASFixedValue.FromFloat(DefaultValue).RawValue;
            if (!DataTag.IsNone)
            {
                return spec.GetSetByCallerMagnitudeRaw(DataTag, WarnIfNotFound, defaultValueRaw);
            }

            if (!string.IsNullOrEmpty(DataName))
            {
                return spec.GetSetByCallerMagnitudeRaw(DataName, WarnIfNotFound, defaultValueRaw);
            }

            if (WarnIfNotFound)
            {
                GASLog.Warning(sb => sb.Append("SetByCallerMagnitude has no DataTag or DataName on effect '")
                    .Append(spec.Def?.Name).Append("'."));
            }

            return defaultValueRaw;
        }
    }

    /// <summary>
    /// An immutable definition for an attribute modifier.
    /// Can use scalable, attribute-based, custom, or SetByCaller magnitude sources.
    /// </summary>
    public class ModifierInfo
    {
        public readonly string AttributeName;
        public readonly EAttributeModifierOperation Operation;
        public readonly GASModifierEvaluationChannel EvaluationChannel;
        public readonly EGameplayEffectMagnitudeCalculation MagnitudeCalculationType;

        public readonly ScalableFloat Magnitude;
        public readonly AttributeBasedMagnitude AttributeBasedMagnitude;
        public readonly GameplayModMagnitudeCalculation CustomCalculation;
        public readonly SetByCallerMagnitude SetByCallerMagnitude;

        /// <summary>
        /// Determines whether the modifier magnitude is snapshotted at application time
        /// or recalculated live each evaluation. Default is Snapshot (UE5 default behavior).
        /// </summary>
        public readonly EGameplayEffectAttributeCaptureSnapshot SnapshotPolicy;

        /// <summary>
        /// Constructor for data-driven, scalable float modifiers.
        /// </summary>
        public ModifierInfo(string attributeName, EAttributeModifierOperation operation, ScalableFloat magnitude)
            : this(attributeName, operation, magnitude, GASModifierEvaluationChannel.Channel0)
        {
        }

        public ModifierInfo(
            string attributeName,
            EAttributeModifierOperation operation,
            ScalableFloat magnitude,
            GASModifierEvaluationChannel evaluationChannel)
        {
            AttributeName = attributeName;
            Operation = operation;
            EvaluationChannel = GASModifierEvaluationChannels.Normalize(evaluationChannel);
            MagnitudeCalculationType = EGameplayEffectMagnitudeCalculation.ScalableFloat;
            Magnitude = magnitude;
            AttributeBasedMagnitude = default;
            CustomCalculation = null;
            SetByCallerMagnitude = default;
            SnapshotPolicy = EGameplayEffectAttributeCaptureSnapshot.Snapshot;
        }

        /// <summary>
        /// Constructor for creating modifiers directly in C# code.
        /// </summary>
        public ModifierInfo(GameplayAttribute attribute, EAttributeModifierOperation operation, ScalableFloat magnitude)
            : this(attribute, operation, magnitude, GASModifierEvaluationChannel.Channel0)
        {
        }

        public ModifierInfo(
            GameplayAttribute attribute,
            EAttributeModifierOperation operation,
            ScalableFloat magnitude,
            GASModifierEvaluationChannel evaluationChannel)
        {
            AttributeName = attribute.Name;
            Operation = operation;
            EvaluationChannel = GASModifierEvaluationChannels.Normalize(evaluationChannel);
            MagnitudeCalculationType = EGameplayEffectMagnitudeCalculation.ScalableFloat;
            Magnitude = magnitude;
            AttributeBasedMagnitude = default;
            CustomCalculation = null;
            SetByCallerMagnitude = default;
            SnapshotPolicy = EGameplayEffectAttributeCaptureSnapshot.Snapshot;
        }

        public ModifierInfo(string attributeName, EAttributeModifierOperation operation, AttributeBasedMagnitude attributeBasedMagnitude)
            : this(attributeName, operation, attributeBasedMagnitude, GASModifierEvaluationChannel.Channel0)
        {
        }

        public ModifierInfo(
            string attributeName,
            EAttributeModifierOperation operation,
            AttributeBasedMagnitude attributeBasedMagnitude,
            GASModifierEvaluationChannel evaluationChannel)
        {
            AttributeName = attributeName;
            Operation = operation;
            EvaluationChannel = GASModifierEvaluationChannels.Normalize(evaluationChannel);
            MagnitudeCalculationType = EGameplayEffectMagnitudeCalculation.AttributeBased;
            Magnitude = default;
            AttributeBasedMagnitude = attributeBasedMagnitude;
            CustomCalculation = null;
            SetByCallerMagnitude = default;
            SnapshotPolicy = attributeBasedMagnitude.SnapshotPolicy;
        }

        public ModifierInfo(GameplayAttribute attribute, EAttributeModifierOperation operation, AttributeBasedMagnitude attributeBasedMagnitude)
            : this(attribute, operation, attributeBasedMagnitude, GASModifierEvaluationChannel.Channel0)
        {
        }

        public ModifierInfo(
            GameplayAttribute attribute,
            EAttributeModifierOperation operation,
            AttributeBasedMagnitude attributeBasedMagnitude,
            GASModifierEvaluationChannel evaluationChannel)
            : this(attribute.Name, operation, attributeBasedMagnitude, evaluationChannel)
        {
        }

        public ModifierInfo(string attributeName, EAttributeModifierOperation operation, SetByCallerMagnitude setByCallerMagnitude)
            : this(attributeName, operation, setByCallerMagnitude, GASModifierEvaluationChannel.Channel0)
        {
        }

        public ModifierInfo(
            string attributeName,
            EAttributeModifierOperation operation,
            SetByCallerMagnitude setByCallerMagnitude,
            GASModifierEvaluationChannel evaluationChannel)
        {
            AttributeName = attributeName;
            Operation = operation;
            EvaluationChannel = GASModifierEvaluationChannels.Normalize(evaluationChannel);
            MagnitudeCalculationType = EGameplayEffectMagnitudeCalculation.SetByCaller;
            Magnitude = default;
            AttributeBasedMagnitude = default;
            CustomCalculation = null;
            SetByCallerMagnitude = setByCallerMagnitude;
            SnapshotPolicy = EGameplayEffectAttributeCaptureSnapshot.Snapshot;
        }

        public ModifierInfo(GameplayAttribute attribute, EAttributeModifierOperation operation, SetByCallerMagnitude setByCallerMagnitude)
            : this(attribute, operation, setByCallerMagnitude, GASModifierEvaluationChannel.Channel0)
        {
        }

        public ModifierInfo(
            GameplayAttribute attribute,
            EAttributeModifierOperation operation,
            SetByCallerMagnitude setByCallerMagnitude,
            GASModifierEvaluationChannel evaluationChannel)
            : this(attribute.Name, operation, setByCallerMagnitude, evaluationChannel)
        {
        }

        public ModifierInfo(string attributeName, EAttributeModifierOperation operation, GameplayModMagnitudeCalculation customCalculation,
            EGameplayEffectAttributeCaptureSnapshot snapshotPolicy = EGameplayEffectAttributeCaptureSnapshot.Snapshot)
            : this(attributeName, operation, customCalculation, snapshotPolicy, GASModifierEvaluationChannel.Channel0)
        {
        }

        public ModifierInfo(
            string attributeName,
            EAttributeModifierOperation operation,
            GameplayModMagnitudeCalculation customCalculation,
            GASModifierEvaluationChannel evaluationChannel,
            EGameplayEffectAttributeCaptureSnapshot snapshotPolicy = EGameplayEffectAttributeCaptureSnapshot.Snapshot)
            : this(attributeName, operation, customCalculation, snapshotPolicy, evaluationChannel)
        {
        }

        public ModifierInfo(
            string attributeName,
            EAttributeModifierOperation operation,
            GameplayModMagnitudeCalculation customCalculation,
            EGameplayEffectAttributeCaptureSnapshot snapshotPolicy,
            GASModifierEvaluationChannel evaluationChannel)
        {
            AttributeName = attributeName;
            Operation = operation;
            EvaluationChannel = GASModifierEvaluationChannels.Normalize(evaluationChannel);
            MagnitudeCalculationType = EGameplayEffectMagnitudeCalculation.CustomCalculation;
            Magnitude = default;
            AttributeBasedMagnitude = default;
            CustomCalculation = customCalculation;
            SetByCallerMagnitude = default;
            SnapshotPolicy = snapshotPolicy;
        }

        public ModifierInfo(GameplayAttribute attribute, EAttributeModifierOperation operation, GameplayModMagnitudeCalculation customCalculation,
            EGameplayEffectAttributeCaptureSnapshot snapshotPolicy = EGameplayEffectAttributeCaptureSnapshot.Snapshot)
            : this(attribute, operation, customCalculation, snapshotPolicy, GASModifierEvaluationChannel.Channel0)
        {
        }

        public ModifierInfo(
            GameplayAttribute attribute,
            EAttributeModifierOperation operation,
            GameplayModMagnitudeCalculation customCalculation,
            GASModifierEvaluationChannel evaluationChannel,
            EGameplayEffectAttributeCaptureSnapshot snapshotPolicy = EGameplayEffectAttributeCaptureSnapshot.Snapshot)
            : this(attribute, operation, customCalculation, snapshotPolicy, evaluationChannel)
        {
        }

        public ModifierInfo(
            GameplayAttribute attribute,
            EAttributeModifierOperation operation,
            GameplayModMagnitudeCalculation customCalculation,
            EGameplayEffectAttributeCaptureSnapshot snapshotPolicy,
            GASModifierEvaluationChannel evaluationChannel)
        {
            AttributeName = attribute.Name;
            Operation = operation;
            EvaluationChannel = GASModifierEvaluationChannels.Normalize(evaluationChannel);
            MagnitudeCalculationType = EGameplayEffectMagnitudeCalculation.CustomCalculation;
            Magnitude = default;
            AttributeBasedMagnitude = default;
            CustomCalculation = customCalculation;
            SetByCallerMagnitude = default;
            SnapshotPolicy = snapshotPolicy;
        }

        public bool ShouldRecalculateLiveMagnitude =>
            SnapshotPolicy == EGameplayEffectAttributeCaptureSnapshot.NotSnapshot
            && MagnitudeCalculationType != EGameplayEffectMagnitudeCalculation.ScalableFloat
            && MagnitudeCalculationType != EGameplayEffectMagnitudeCalculation.SetByCaller;

        public bool ShouldRecalculateWhenTargetAssigned =>
            MagnitudeCalculationType == EGameplayEffectMagnitudeCalculation.CustomCalculation
            || (MagnitudeCalculationType == EGameplayEffectMagnitudeCalculation.AttributeBased
                && AttributeBasedMagnitude.CaptureSource == EGameplayEffectAttributeCaptureSource.Target);

        public bool DependsOnLiveAttribute(GameplayAttribute attribute, GameplayEffectSpec spec)
        {
            return MagnitudeCalculationType == EGameplayEffectMagnitudeCalculation.AttributeBased
                && SnapshotPolicy == EGameplayEffectAttributeCaptureSnapshot.NotSnapshot
                && AttributeBasedMagnitude.DependsOnAttribute(attribute, spec);
        }

        public long CalculateMagnitudeRaw(GameplayEffectSpec spec, int level)
        {
            switch (MagnitudeCalculationType)
            {
                case EGameplayEffectMagnitudeCalculation.AttributeBased:
                    return AttributeBasedMagnitude.CalculateMagnitudeRaw(spec, level);
                case EGameplayEffectMagnitudeCalculation.CustomCalculation:
                    return GASFixedValue.FromFloat(CustomCalculation != null ? CustomCalculation.CalculateMagnitude(spec) : 0f).RawValue;
                case EGameplayEffectMagnitudeCalculation.SetByCaller:
                    return SetByCallerMagnitude.CalculateMagnitudeRaw(spec);
                default:
                    return GASFixedValue.FromFloat(Magnitude.GetValueAtLevel(level)).RawValue;
            }
        }

        public float CalculateMagnitude(GameplayEffectSpec spec, int level)
        {
            return GASFixedValue.FromRaw(CalculateMagnitudeRaw(spec, level)).ToFloat();
        }
    }
}
