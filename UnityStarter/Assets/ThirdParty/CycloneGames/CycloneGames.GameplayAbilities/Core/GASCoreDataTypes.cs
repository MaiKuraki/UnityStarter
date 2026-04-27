using System;

namespace CycloneGames.GameplayAbilities.Core
{
    public readonly struct GASAbilitySpecData
    {
        public readonly GASSpecHandle Handle;
        public readonly GASDefinitionId AbilityDefinitionId;
        public readonly ushort Level;
        public readonly GASInstancingPolicy InstancingPolicy;
        public readonly GASNetExecutionPolicy NetExecutionPolicy;
        public readonly GASReplicationPolicy ReplicationPolicy;

        public GASAbilitySpecData(
            GASSpecHandle handle,
            GASDefinitionId abilityDefinitionId,
            ushort level,
            GASInstancingPolicy instancingPolicy,
            GASNetExecutionPolicy netExecutionPolicy,
            GASReplicationPolicy replicationPolicy)
        {
            Handle = handle;
            AbilityDefinitionId = abilityDefinitionId;
            Level = level;
            InstancingPolicy = instancingPolicy;
            NetExecutionPolicy = netExecutionPolicy;
            ReplicationPolicy = replicationPolicy;
        }
    }

    public readonly struct GASAttributeValueData
    {
        public readonly GASAttributeId AttributeId;
        public readonly float BaseValue;
        public readonly float CurrentValue;
        public readonly uint AggregatorVersion;

        public GASAttributeValueData(GASAttributeId attributeId, float baseValue, float currentValue, uint aggregatorVersion)
        {
            AttributeId = attributeId;
            BaseValue = baseValue;
            CurrentValue = currentValue;
            AggregatorVersion = aggregatorVersion;
        }
    }

    public readonly struct GASModifierData
    {
        public readonly GASAttributeId AttributeId;
        public readonly GASModifierOp Op;
        public readonly float Magnitude;

        public GASModifierData(GASAttributeId attributeId, GASModifierOp op, float magnitude)
        {
            AttributeId = attributeId;
            Op = op;
            Magnitude = magnitude;
        }
    }

    public readonly struct GASAbilityGrantRequest
    {
        public readonly GASDefinitionId AbilityDefinitionId;
        public readonly ushort Level;
        public readonly GASInstancingPolicy InstancingPolicy;
        public readonly GASNetExecutionPolicy NetExecutionPolicy;
        public readonly GASReplicationPolicy ReplicationPolicy;

        public GASAbilityGrantRequest(
            GASDefinitionId abilityDefinitionId,
            ushort level,
            GASInstancingPolicy instancingPolicy,
            GASNetExecutionPolicy netExecutionPolicy,
            GASReplicationPolicy replicationPolicy)
        {
            AbilityDefinitionId = abilityDefinitionId;
            Level = level;
            InstancingPolicy = instancingPolicy;
            NetExecutionPolicy = netExecutionPolicy;
            ReplicationPolicy = replicationPolicy;
        }
    }

    /// <summary>
    /// Packed gameplay effect specification for processing.
    /// ModifierStart and ModifierCount define a slice into a shared Modifiers array —
    /// multiple specs can reference non-overlapping ranges of the same pre-allocated
    /// buffer, avoiding per-spec array allocations on the hot path.
    /// </summary>
    public readonly struct GASGameplayEffectSpecData
    {
        public readonly GASDefinitionId EffectDefinitionId;
        public readonly GASEntityId Source;
        public readonly GASPredictionKey PredictionKey;
        public readonly GASEffectDurationPolicy DurationPolicy;
        public readonly ushort Level;
        public readonly ushort StackCount;
        public readonly int StartTick;
        public readonly int DurationTicks;
        public readonly GASModifierData[] Modifiers;
        public readonly int ModifierStart;
        public readonly int ModifierCount;

        public GASGameplayEffectSpecData(
            GASDefinitionId effectDefinitionId,
            GASEntityId source,
            GASPredictionKey predictionKey,
            GASEffectDurationPolicy durationPolicy,
            ushort level,
            ushort stackCount,
            int startTick,
            int durationTicks,
            GASModifierData[] modifiers,
            int modifierStart,
            int modifierCount)
        {
            EffectDefinitionId = effectDefinitionId;
            Source = source;
            PredictionKey = predictionKey;
            DurationPolicy = durationPolicy;
            Level = level;
            StackCount = stackCount;
            StartTick = startTick;
            DurationTicks = durationTicks;
            Modifiers = modifiers;
            ModifierStart = modifierStart;
            ModifierCount = modifierCount;
        }
    }

    public readonly struct GASAbilityActivationResult
    {
        public readonly GASAbilityActivationResultCode Code;
        public readonly GASSpecHandle SpecHandle;
        public readonly GASPredictionKey PredictionKey;

        public bool Succeeded => Code == GASAbilityActivationResultCode.Accepted || Code == GASAbilityActivationResultCode.Predicted;

        public GASAbilityActivationResult(GASAbilityActivationResultCode code, GASSpecHandle specHandle, GASPredictionKey predictionKey)
        {
            Code = code;
            SpecHandle = specHandle;
            PredictionKey = predictionKey;
        }
    }

    internal readonly struct GASPredictedAttributeChange
    {
        public readonly GASPredictionKey PredictionKey;
        public readonly GASAttributeId AttributeId;
        public readonly float OldBaseValue;

        public GASPredictedAttributeChange(GASPredictionKey predictionKey, GASAttributeId attributeId, float oldBaseValue)
        {
            PredictionKey = predictionKey;
            AttributeId = attributeId;
            OldBaseValue = oldBaseValue;
        }
    }

    public readonly struct GASActiveEffectData
    {
        public readonly GASActiveEffectHandle Handle;
        public readonly GASDefinitionId EffectDefinitionId;
        public readonly GASEntityId Source;
        public readonly GASEntityId Target;
        public readonly GASPredictionKey PredictionKey;
        public readonly GASEffectDurationPolicy DurationPolicy;
        public readonly ushort Level;
        public readonly ushort StackCount;
        public readonly int StartTick;
        public readonly int DurationTicks;
        public readonly uint ModifierStartIndex;
        public readonly ushort ModifierCount;

        public GASActiveEffectData(
            GASActiveEffectHandle handle,
            GASDefinitionId effectDefinitionId,
            GASEntityId source,
            GASEntityId target,
            GASPredictionKey predictionKey,
            GASEffectDurationPolicy durationPolicy,
            ushort level,
            ushort stackCount,
            int startTick,
            int durationTicks,
            uint modifierStartIndex,
            ushort modifierCount)
        {
            Handle = handle;
            EffectDefinitionId = effectDefinitionId;
            Source = source;
            Target = target;
            PredictionKey = predictionKey;
            DurationPolicy = durationPolicy;
            Level = level;
            StackCount = stackCount;
            StartTick = startTick;
            DurationTicks = durationTicks;
            ModifierStartIndex = modifierStartIndex;
            ModifierCount = modifierCount;
        }
    }

    /// <summary>
    /// Per-domain state checksums used for network change detection.
    /// The Combined property folds four independent domain checksums into one
    /// using FNV-1a hash combining (multiplier 16777619, offset basis 2166136261).
    /// Individual domain checksums allow the network layer to determine which
    /// subsystems changed without hashing the entire state.
    /// </summary>
    public readonly struct GASStateChecksum
    {
        public readonly uint Abilities;
        public readonly uint Attributes;
        public readonly uint Effects;
        public readonly uint Tags;

        public uint Combined
        {
            get
            {
                unchecked
                {
                    uint hash = 2166136261u;
                    hash = (hash ^ Abilities) * 16777619u;
                    hash = (hash ^ Attributes) * 16777619u;
                    hash = (hash ^ Effects) * 16777619u;
                    hash = (hash ^ Tags) * 16777619u;
                    return hash;
                }
            }
        }

        public GASStateChecksum(uint abilities, uint attributes, uint effects, uint tags)
        {
            Abilities = abilities;
            Attributes = attributes;
            Effects = effects;
            Tags = tags;
        }
    }

    /// <summary>
    /// Reusable caller-owned buffer for capturing a snapshot of ASC state.
    /// Callers must treat only [0, Count) entries as valid in each array section.
    /// Designed for CaptureStateNonAlloc-style usage: create once, reuse across frames
    /// to avoid per-frame array allocations.
    /// </summary>
    public sealed class GASAbilitySystemStateBuffer
    {
        public GASEntityId Entity;
        public ulong Version;
        public GASStateChecksum Checksum;

        public GASAbilitySpecData[] AbilitySpecs = Array.Empty<GASAbilitySpecData>();
        public int AbilitySpecCount;

        public GASAttributeValueData[] Attributes = Array.Empty<GASAttributeValueData>();
        public int AttributeCount;

        public GASActiveEffectData[] ActiveEffects = Array.Empty<GASActiveEffectData>();
        public int ActiveEffectCount;

        public GASModifierData[] Modifiers = Array.Empty<GASModifierData>();
        public int ModifierCount;

        public void ClearCounts()
        {
            Entity = default;
            Version = 0;
            Checksum = default;
            AbilitySpecCount = 0;
            AttributeCount = 0;
            ActiveEffectCount = 0;
            ModifierCount = 0;
        }

        public GASAbilitySpecData[] EnsureAbilitySpecCapacity(int capacity)
        {
            if (AbilitySpecs.Length < capacity)
            {
                AbilitySpecs = new GASAbilitySpecData[capacity];
            }

            return AbilitySpecs;
        }

        public GASAttributeValueData[] EnsureAttributeCapacity(int capacity)
        {
            if (Attributes.Length < capacity)
            {
                Attributes = new GASAttributeValueData[capacity];
            }

            return Attributes;
        }

        public GASActiveEffectData[] EnsureActiveEffectCapacity(int capacity)
        {
            if (ActiveEffects.Length < capacity)
            {
                ActiveEffects = new GASActiveEffectData[capacity];
            }

            return ActiveEffects;
        }

        public GASModifierData[] EnsureModifierCapacity(int capacity)
        {
            if (Modifiers.Length < capacity)
            {
                Modifiers = new GASModifierData[capacity];
            }

            return Modifiers;
        }
    }

    public readonly struct GASAttributeDefinition
    {
        public readonly GASAttributeId Id;
        public readonly string StableName;
        public readonly uint ContentHash;

        public GASAttributeDefinition(GASAttributeId id, string stableName, uint contentHash)
        {
            Id = id;
            StableName = stableName;
            ContentHash = contentHash;
        }
    }
}
