namespace CycloneGames.GameplayAbilities.Runtime
{
    public static class GameplayEffectConstants
    {
        public const float INFINITE_DURATION = -1.0f;
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

        public GameplayEffectStacking(EGameplayEffectStackingType type, int limit, EGameplayEffectStackingDurationPolicy durationPolicy)
        {
            Type = type;
            Limit = limit;
            DurationPolicy = durationPolicy;
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
        /// Constructor for data-driven, scalable float modifiers.
        /// </summary>
        public ModifierInfo(string attributeName, EAttributeModifierOperation operation, ScalableFloat magnitude)
        {
            AttributeName = attributeName;
            Operation = operation;
            Magnitude = magnitude;
            CustomCalculation = null; // Ensure the other is null
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
        }

        public ModifierInfo(string attributeName, EAttributeModifierOperation operation, GameplayModMagnitudeCalculation customCalculation)
        {
            AttributeName = attributeName;
            Operation = operation;
            Magnitude = default; // Ensure the other is default
            CustomCalculation = customCalculation;
        }

        public ModifierInfo(GameplayAttribute attribute, EAttributeModifierOperation operation, GameplayModMagnitudeCalculation customCalculation)
        {
            AttributeName = attribute.Name;
            Operation = operation;
            Magnitude = default; // Ensure the other is default
            CustomCalculation = customCalculation;
        }
    }
}