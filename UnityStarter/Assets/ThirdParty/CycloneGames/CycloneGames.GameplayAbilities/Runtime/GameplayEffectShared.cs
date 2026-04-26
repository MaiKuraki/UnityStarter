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
    /// An immutable definition for an attribute modifier.
    /// Can use a simple ScalableFloat or a complex custom calculation class.
    /// </summary>
    public class ModifierInfo
    {
        public readonly string AttributeName;
        public readonly EAttributeModifierOperation Operation;

        // One of these two will be used for calculation.
        public readonly ScalableFloat Magnitude;
        public readonly GameplayModMagnitudeCalculation CustomCalculation;

        /// <summary>
        /// Determines whether the modifier magnitude is snapshotted at application time
        /// or recalculated live each evaluation. Default is Snapshot (UE5 default behavior).
        /// Only meaningful for modifiers using CustomCalculation.
        /// </summary>
        public readonly EGameplayEffectAttributeCaptureSnapshot SnapshotPolicy;

        /// <summary>
        /// Constructor for data-driven, scalable float modifiers.
        /// </summary>
        public ModifierInfo(string attributeName, EAttributeModifierOperation operation, ScalableFloat magnitude)
        {
            AttributeName = attributeName;
            Operation = operation;
            Magnitude = magnitude;
            CustomCalculation = null;
            SnapshotPolicy = EGameplayEffectAttributeCaptureSnapshot.Snapshot;
        }

        /// <summary>
        /// Constructor for creating modifiers directly in C# code.
        /// </summary>
        public ModifierInfo(GameplayAttribute attribute, EAttributeModifierOperation operation, ScalableFloat magnitude)
        {
            AttributeName = attribute.Name;
            Operation = operation;
            Magnitude = magnitude;
            CustomCalculation = null;
            SnapshotPolicy = EGameplayEffectAttributeCaptureSnapshot.Snapshot;
        }

        public ModifierInfo(string attributeName, EAttributeModifierOperation operation, GameplayModMagnitudeCalculation customCalculation,
            EGameplayEffectAttributeCaptureSnapshot snapshotPolicy = EGameplayEffectAttributeCaptureSnapshot.Snapshot)
        {
            AttributeName = attributeName;
            Operation = operation;
            Magnitude = default;
            CustomCalculation = customCalculation;
            SnapshotPolicy = snapshotPolicy;
        }

        public ModifierInfo(GameplayAttribute attribute, EAttributeModifierOperation operation, GameplayModMagnitudeCalculation customCalculation,
            EGameplayEffectAttributeCaptureSnapshot snapshotPolicy = EGameplayEffectAttributeCaptureSnapshot.Snapshot)
        {
            AttributeName = attribute.Name;
            Operation = operation;
            Magnitude = default;
            CustomCalculation = customCalculation;
            SnapshotPolicy = snapshotPolicy;
        }
    }
}