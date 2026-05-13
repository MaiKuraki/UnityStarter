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
        public readonly long ValueRaw;
        public GASFixedValue Value => GASFixedValue.FromRaw(ValueRaw);

        private GASSetByCallerTagStateData(GameplayTags.Core.GameplayTag tag, long valueRaw)
        {
            Tag = tag;
            ValueRaw = valueRaw;
        }

        public static GASSetByCallerTagStateData FromRaw(GameplayTags.Core.GameplayTag tag, long valueRaw)
        {
            return new GASSetByCallerTagStateData(tag, valueRaw);
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
        public readonly long DurationRaw;
        public readonly long TimeRemainingRaw;
        public readonly long PeriodTimeRemainingRaw;
        public readonly GASPredictionKey PredictionKey;
        public readonly GASSetByCallerTagStateData[] SetByCallerTagMagnitudes;
        public readonly int SetByCallerTagMagnitudeCount;
        public GASFixedValue Duration => GASFixedValue.FromRaw(DurationRaw);
        public GASFixedValue TimeRemaining => GASFixedValue.FromRaw(TimeRemainingRaw);
        public GASFixedValue PeriodTimeRemaining => GASFixedValue.FromRaw(PeriodTimeRemainingRaw);

        private GASActiveEffectStateData(
            int instanceId,
            object effectDefinition,
            object sourceComponent,
            int level,
            int stackCount,
            long durationRaw,
            long timeRemainingRaw,
            long periodTimeRemainingRaw,
            GASPredictionKey predictionKey,
            GASSetByCallerTagStateData[] setByCallerTagMagnitudes,
            int setByCallerTagMagnitudeCount)
        {
            InstanceId = instanceId;
            EffectDefinition = effectDefinition;
            SourceComponent = sourceComponent;
            Level = level;
            StackCount = stackCount;
            DurationRaw = durationRaw;
            TimeRemainingRaw = timeRemainingRaw;
            PeriodTimeRemainingRaw = periodTimeRemainingRaw;
            PredictionKey = predictionKey;
            SetByCallerTagMagnitudes = setByCallerTagMagnitudes;
            SetByCallerTagMagnitudeCount = setByCallerTagMagnitudeCount < 0 ? 0 : setByCallerTagMagnitudeCount;
        }

        public static GASActiveEffectStateData FromRaw(
            int instanceId,
            object effectDefinition,
            object sourceComponent,
            int level,
            int stackCount,
            long durationRaw,
            long timeRemainingRaw,
            long periodTimeRemainingRaw,
            GASPredictionKey predictionKey,
            GASSetByCallerTagStateData[] setByCallerTagMagnitudes,
            int setByCallerTagMagnitudeCount)
        {
            return new GASActiveEffectStateData(
                instanceId,
                effectDefinition,
                sourceComponent,
                level,
                stackCount,
                durationRaw,
                timeRemainingRaw,
                periodTimeRemainingRaw,
                predictionKey,
                setByCallerTagMagnitudes,
                setByCallerTagMagnitudeCount);
        }
    }

    /// <summary>
    /// Pure C# snapshot of an attribute value pair.
    /// </summary>
    public readonly struct GASAttributeStateData
    {
        public readonly string AttributeName;
        public readonly long BaseValueRaw;
        public readonly long CurrentValueRaw;
        public GASFixedValue BaseValue => GASFixedValue.FromRaw(BaseValueRaw);
        public GASFixedValue CurrentValue => GASFixedValue.FromRaw(CurrentValueRaw);

        private GASAttributeStateData(string attributeName, long baseValueRaw, long currentValueRaw)
        {
            AttributeName = attributeName;
            BaseValueRaw = baseValueRaw;
            CurrentValueRaw = currentValueRaw;
        }

        public static GASAttributeStateData FromRaw(string attributeName, long baseValueRaw, long currentValueRaw)
        {
            return new GASAttributeStateData(attributeName, baseValueRaw, currentValueRaw);
        }
    }
}
