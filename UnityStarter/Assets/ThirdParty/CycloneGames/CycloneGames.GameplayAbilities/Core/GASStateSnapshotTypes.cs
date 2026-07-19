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
        public readonly bool IsInputPressed;
        public readonly int GrantingEffectReconciliationId;

        public GASGrantedAbilityStateData(IGASAbilityDefinition abilityDefinition, int level, bool isActive)
            : this(0, abilityDefinition, level, isActive, false, 0)
        {
        }

        public GASGrantedAbilityStateData(int specHandle, IGASAbilityDefinition abilityDefinition, int level, bool isActive)
            : this(specHandle, abilityDefinition, level, isActive, false, 0)
        {
        }

        public GASGrantedAbilityStateData(
            int specHandle,
            IGASAbilityDefinition abilityDefinition,
            int level,
            bool isActive,
            bool isInputPressed,
            int grantingEffectReconciliationId)
        {
            SpecHandle = specHandle;
            AbilityDefinition = abilityDefinition;
            Level = level;
            IsActive = isActive;
            IsInputPressed = isInputPressed;
            GrantingEffectReconciliationId = grantingEffectReconciliationId;
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

    public readonly struct GASSetByCallerNameStateData
    {
        public readonly string Name;
        public readonly long ValueRaw;
        public GASFixedValue Value => GASFixedValue.FromRaw(ValueRaw);

        private GASSetByCallerNameStateData(string name, long valueRaw)
        {
            Name = name;
            ValueRaw = valueRaw;
        }

        public static GASSetByCallerNameStateData FromRaw(string name, long valueRaw)
        {
            return new GASSetByCallerNameStateData(name, valueRaw);
        }
    }

    /// <summary>
    /// Pure C# process-local reconciliation state for an active gameplay effect.
    /// </summary>
    public readonly struct GASActiveEffectStateData
    {
        public readonly int ReconciliationId;
        public readonly object EffectDefinition;
        public readonly object SourceComponent;
        public readonly int SourceAbilitySpecHandle;
        public readonly int Level;
        public readonly int StackCount;
        public readonly bool IsInhibited;
        public readonly long DurationRaw;
        public readonly long TimeRemainingRaw;
        public readonly long PeriodTimeRemainingRaw;
        public readonly GASPredictionKey PredictionKey;
        public readonly GASSetByCallerTagStateData[] SetByCallerTagMagnitudes;
        public readonly int SetByCallerTagMagnitudeCount;
        public readonly GASSetByCallerNameStateData[] SetByCallerNameMagnitudes;
        public readonly int SetByCallerNameMagnitudeCount;
        public readonly GameplayTags.Core.GameplayTag[] DynamicGrantedTags;
        public readonly int DynamicGrantedTagCount;
        public readonly GameplayTags.Core.GameplayTag[] DynamicAssetTags;
        public readonly int DynamicAssetTagCount;
        public GASFixedValue Duration => GASFixedValue.FromRaw(DurationRaw);
        public GASFixedValue TimeRemaining => GASFixedValue.FromRaw(TimeRemainingRaw);
        public GASFixedValue PeriodTimeRemaining => GASFixedValue.FromRaw(PeriodTimeRemainingRaw);

        private GASActiveEffectStateData(
            int reconciliationId,
            object effectDefinition,
            object sourceComponent,
            int sourceAbilitySpecHandle,
            int level,
            int stackCount,
            bool isInhibited,
            long durationRaw,
            long timeRemainingRaw,
            long periodTimeRemainingRaw,
            GASPredictionKey predictionKey,
            GASSetByCallerTagStateData[] setByCallerTagMagnitudes,
            int setByCallerTagMagnitudeCount,
            GASSetByCallerNameStateData[] setByCallerNameMagnitudes,
            int setByCallerNameMagnitudeCount,
            GameplayTags.Core.GameplayTag[] dynamicGrantedTags,
            int dynamicGrantedTagCount,
            GameplayTags.Core.GameplayTag[] dynamicAssetTags,
            int dynamicAssetTagCount)
        {
            ReconciliationId = reconciliationId;
            EffectDefinition = effectDefinition;
            SourceComponent = sourceComponent;
            SourceAbilitySpecHandle = sourceAbilitySpecHandle;
            Level = level;
            StackCount = stackCount;
            IsInhibited = isInhibited;
            DurationRaw = durationRaw;
            TimeRemainingRaw = timeRemainingRaw;
            PeriodTimeRemainingRaw = periodTimeRemainingRaw;
            PredictionKey = predictionKey;
            SetByCallerTagMagnitudes = setByCallerTagMagnitudes;
            SetByCallerTagMagnitudeCount = setByCallerTagMagnitudeCount < 0 ? 0 : setByCallerTagMagnitudeCount;
            SetByCallerNameMagnitudes = setByCallerNameMagnitudes;
            SetByCallerNameMagnitudeCount = setByCallerNameMagnitudeCount < 0 ? 0 : setByCallerNameMagnitudeCount;
            DynamicGrantedTags = dynamicGrantedTags;
            DynamicGrantedTagCount = dynamicGrantedTagCount < 0 ? 0 : dynamicGrantedTagCount;
            DynamicAssetTags = dynamicAssetTags;
            DynamicAssetTagCount = dynamicAssetTagCount < 0 ? 0 : dynamicAssetTagCount;
        }

        public static GASActiveEffectStateData FromRaw(
            int reconciliationId,
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
            return FromRaw(
                reconciliationId,
                effectDefinition,
                sourceComponent,
                0,
                level,
                stackCount,
                false,
                durationRaw,
                timeRemainingRaw,
                periodTimeRemainingRaw,
                predictionKey,
                setByCallerTagMagnitudes,
                setByCallerTagMagnitudeCount,
                Array.Empty<GASSetByCallerNameStateData>(),
                0,
                Array.Empty<GameplayTags.Core.GameplayTag>(),
                0,
                Array.Empty<GameplayTags.Core.GameplayTag>(),
                0);
        }

        public static GASActiveEffectStateData FromRaw(
            int reconciliationId,
            object effectDefinition,
            object sourceComponent,
            int sourceAbilitySpecHandle,
            int level,
            int stackCount,
            bool isInhibited,
            long durationRaw,
            long timeRemainingRaw,
            long periodTimeRemainingRaw,
            GASPredictionKey predictionKey,
            GASSetByCallerTagStateData[] setByCallerTagMagnitudes,
            int setByCallerTagMagnitudeCount,
            GASSetByCallerNameStateData[] setByCallerNameMagnitudes,
            int setByCallerNameMagnitudeCount,
            GameplayTags.Core.GameplayTag[] dynamicGrantedTags,
            int dynamicGrantedTagCount,
            GameplayTags.Core.GameplayTag[] dynamicAssetTags,
            int dynamicAssetTagCount)
        {
            return new GASActiveEffectStateData(
                reconciliationId,
                effectDefinition,
                sourceComponent,
                sourceAbilitySpecHandle,
                level,
                stackCount,
                isInhibited,
                durationRaw,
                timeRemainingRaw,
                periodTimeRemainingRaw,
                predictionKey,
                setByCallerTagMagnitudes,
                setByCallerTagMagnitudeCount,
                setByCallerNameMagnitudes,
                setByCallerNameMagnitudeCount,
                dynamicGrantedTags,
                dynamicGrantedTagCount,
                dynamicAssetTags,
                dynamicAssetTagCount);
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
