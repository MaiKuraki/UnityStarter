using System;

namespace CycloneGames.GameplayAbilities.Core
{
    /// <summary>
    /// Marker interface for ability definition types.
    /// Constraining snapshot fields to this interface prevents accidental boxing of value types
    /// and makes the API intent explicit: only reference-type definitions are valid.
    /// </summary>
    public interface IGASAbilityDefinition { }

    [Flags]
    public enum AbilitySystemStateChangeMask
    {
        None = 0,
        GrantedAbilities = 1 << 0,
        ActiveEffects = 1 << 1,
        Attributes = 1 << 2,
        Tags = 1 << 3
    }

    /// <summary>
    /// Pure C# snapshot of a granted ability entry.
    /// The definition reference is opaque so adapters can map it to engine- or network-specific IDs.
    /// </summary>
    public readonly struct GASGrantedAbilityStateData
    {
        public readonly int SpecHandle;
        public readonly IGASAbilityDefinition AbilityDefinition;
        public readonly int Level;
        public readonly bool IsActive;

        public GASGrantedAbilityStateData(IGASAbilityDefinition abilityDefinition, int level, bool isActive)
            : this(0, abilityDefinition, level, isActive)
        {
        }

        public GASGrantedAbilityStateData(int specHandle, IGASAbilityDefinition abilityDefinition, int level, bool isActive)
        {
            SpecHandle = specHandle;
            AbilityDefinition = abilityDefinition;
            Level = level;
            IsActive = isActive;
        }
    }

    /// <summary>
    /// Pure C# snapshot of a single SetByCaller magnitude addressed by GameplayTag.
    /// </summary>
    public readonly struct GASSetByCallerTagStateData
    {
        public readonly GameplayTags.Core.GameplayTag Tag;
        public readonly float Value;

        public GASSetByCallerTagStateData(GameplayTags.Core.GameplayTag tag, float value)
        {
            Tag = tag;
            Value = value;
        }
    }

    /// <summary>
    /// Pure C# snapshot of an active gameplay effect.
    /// </summary>
    public readonly struct GASActiveEffectStateData
    {
        public readonly int InstanceId;
        public readonly object EffectDefinition;
        public readonly object SourceComponent;
        public readonly int Level;
        public readonly int StackCount;
        public readonly float Duration;
        public readonly float TimeRemaining;
        public readonly float PeriodTimeRemaining;
        public readonly GASPredictionKey PredictionKey;
        public readonly GASSetByCallerTagStateData[] SetByCallerTagMagnitudes;
        public readonly int SetByCallerTagMagnitudeCount;

        public GASActiveEffectStateData(
            int instanceId,
            object effectDefinition,
            object sourceComponent,
            int level,
            int stackCount,
            float duration,
            float timeRemaining,
            float periodTimeRemaining,
            GASPredictionKey predictionKey,
            GASSetByCallerTagStateData[] setByCallerTagMagnitudes)
            : this(
                instanceId,
                effectDefinition,
                sourceComponent,
                level,
                stackCount,
                duration,
                timeRemaining,
                periodTimeRemaining,
                predictionKey,
                setByCallerTagMagnitudes,
                setByCallerTagMagnitudes != null ? setByCallerTagMagnitudes.Length : 0)
        {
        }

        public GASActiveEffectStateData(
            int instanceId,
            object effectDefinition,
            object sourceComponent,
            int level,
            int stackCount,
            float duration,
            float timeRemaining,
            float periodTimeRemaining,
            GASPredictionKey predictionKey,
            GASSetByCallerTagStateData[] setByCallerTagMagnitudes,
            int setByCallerTagMagnitudeCount)
        {
            InstanceId = instanceId;
            EffectDefinition = effectDefinition;
            SourceComponent = sourceComponent;
            Level = level;
            StackCount = stackCount;
            Duration = duration;
            TimeRemaining = timeRemaining;
            PeriodTimeRemaining = periodTimeRemaining;
            PredictionKey = predictionKey;
            SetByCallerTagMagnitudes = setByCallerTagMagnitudes;
            SetByCallerTagMagnitudeCount = setByCallerTagMagnitudeCount < 0 ? 0 : setByCallerTagMagnitudeCount;
        }
    }

    /// <summary>
    /// Pure C# snapshot of an attribute value pair.
    /// </summary>
    public readonly struct GASAttributeStateData
    {
        public readonly string AttributeName;
        public readonly float BaseValue;
        public readonly float CurrentValue;

        public GASAttributeStateData(string attributeName, float baseValue, float currentValue)
        {
            AttributeName = attributeName;
            BaseValue = baseValue;
            CurrentValue = currentValue;
        }
    }
}
