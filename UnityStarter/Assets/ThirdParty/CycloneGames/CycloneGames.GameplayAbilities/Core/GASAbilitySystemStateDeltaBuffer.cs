using System;
using CycloneGames.GameplayTags.Core;

namespace CycloneGames.GameplayAbilities.Core
{
    /// <summary>
    /// Reusable buffer for carrying incremental state changes to a single client.
    /// 
    /// Owned by the caller (typically the network bridge). Each public array has a
    /// companion Count field — only indices [0, Count) contain valid data. The
    /// Reserve method pre-allocates internal arrays so the hot path never allocates
    /// during state capture. Call ClearCounts() to reuse the same buffer instance.
    /// </summary>
    public sealed class GASAbilitySystemStateDeltaBuffer
    {
        public uint Sequence;
        public uint StateChecksum;
        public ulong BaseVersion;
        public ulong CurrentVersion;
        public AbilitySystemStateChangeMask ChangeMask;

        public GASGrantedAbilityStateData[] GrantedAbilities = Array.Empty<GASGrantedAbilityStateData>();
        public int GrantedAbilityCount;

        public IGASAbilityDefinition[] RemovedAbilityDefinitions = Array.Empty<IGASAbilityDefinition>();
        public int RemovedAbilityDefinitionCount;

        public GASActiveEffectStateData[] ActiveEffects = Array.Empty<GASActiveEffectStateData>();
        public int ActiveEffectCount;
        public GASSetByCallerTagStateData[][] ActiveEffectSetByCallerMagnitudes = Array.Empty<GASSetByCallerTagStateData[]>();

        public int[] RemovedEffectNetIds = Array.Empty<int>();
        public int RemovedEffectNetIdCount;

        public GASAttributeStateData[] Attributes = Array.Empty<GASAttributeStateData>();
        public int AttributeCount;

        public GameplayTag[] AddedTags = Array.Empty<GameplayTag>();
        public int AddedTagCount;

        public GameplayTag[] RemovedTags = Array.Empty<GameplayTag>();
        public int RemovedTagCount;

        public bool HasChanges => ChangeMask != AbilitySystemStateChangeMask.None;

        public void Reserve(
            int grantedAbilityCapacity,
            int removedAbilityDefinitionCapacity,
            int activeEffectCapacity,
            int removedEffectCapacity,
            int attributeCapacity,
            int addedTagCapacity,
            int removedTagCapacity,
            int maxSetByCallerPerEffect = 0)
        {
            EnsureGrantedAbilityCapacity(grantedAbilityCapacity);
            EnsureRemovedAbilityDefinitionCapacity(removedAbilityDefinitionCapacity);
            EnsureActiveEffectCapacity(activeEffectCapacity);
            EnsureRemovedEffectNetIdCapacity(removedEffectCapacity);
            EnsureAttributeCapacity(attributeCapacity);
            EnsureAddedTagCapacity(addedTagCapacity);
            EnsureRemovedTagCapacity(removedTagCapacity);

            if (maxSetByCallerPerEffect > 0)
            {
                for (int i = 0; i < activeEffectCapacity; i++)
                {
                    EnsureActiveEffectSetByCallerCapacity(i, maxSetByCallerPerEffect);
                }
            }
        }

        public void ClearCounts()
        {
            Sequence = 0;
            StateChecksum = 0;
            BaseVersion = 0;
            CurrentVersion = 0;
            ChangeMask = AbilitySystemStateChangeMask.None;
            GrantedAbilityCount = 0;
            RemovedAbilityDefinitionCount = 0;
            ActiveEffectCount = 0;
            RemovedEffectNetIdCount = 0;
            AttributeCount = 0;
            AddedTagCount = 0;
            RemovedTagCount = 0;
        }

        public GASGrantedAbilityStateData[] EnsureGrantedAbilityCapacity(int capacity)
        {
            if (GrantedAbilities.Length < capacity)
            {
                GrantedAbilities = new GASGrantedAbilityStateData[capacity];
            }

            return GrantedAbilities;
        }

        public IGASAbilityDefinition[] EnsureRemovedAbilityDefinitionCapacity(int capacity)
        {
            if (RemovedAbilityDefinitions.Length < capacity)
            {
                RemovedAbilityDefinitions = new IGASAbilityDefinition[capacity];
            }

            return RemovedAbilityDefinitions;
        }

        public GASActiveEffectStateData[] EnsureActiveEffectCapacity(int capacity)
        {
            if (ActiveEffects.Length < capacity)
            {
                ActiveEffects = new GASActiveEffectStateData[capacity];
            }

            if (ActiveEffectSetByCallerMagnitudes.Length < capacity)
            {
                var existing = ActiveEffectSetByCallerMagnitudes;
                ActiveEffectSetByCallerMagnitudes = new GASSetByCallerTagStateData[capacity][];
                for (int i = 0; i < existing.Length; i++)
                {
                    ActiveEffectSetByCallerMagnitudes[i] = existing[i];
                }
            }

            return ActiveEffects;
        }

        public GASSetByCallerTagStateData[] EnsureActiveEffectSetByCallerCapacity(int effectIndex, int capacity)
        {
            if (effectIndex < 0)
            {
                return Array.Empty<GASSetByCallerTagStateData>();
            }

            if (ActiveEffectSetByCallerMagnitudes.Length <= effectIndex)
            {
                EnsureActiveEffectCapacity(effectIndex + 1);
            }

            var entries = ActiveEffectSetByCallerMagnitudes[effectIndex];
            if (entries == null || entries.Length < capacity)
            {
                entries = new GASSetByCallerTagStateData[capacity];
                ActiveEffectSetByCallerMagnitudes[effectIndex] = entries;
            }

            return entries;
        }

        public int[] EnsureRemovedEffectNetIdCapacity(int capacity)
        {
            if (RemovedEffectNetIds.Length < capacity)
            {
                RemovedEffectNetIds = new int[capacity];
            }

            return RemovedEffectNetIds;
        }

        public GASAttributeStateData[] EnsureAttributeCapacity(int capacity)
        {
            if (Attributes.Length < capacity)
            {
                Attributes = new GASAttributeStateData[capacity];
            }

            return Attributes;
        }

        public GameplayTag[] EnsureAddedTagCapacity(int capacity)
        {
            if (AddedTags.Length < capacity)
            {
                AddedTags = new GameplayTag[capacity];
            }

            return AddedTags;
        }

        public GameplayTag[] EnsureRemovedTagCapacity(int capacity)
        {
            if (RemovedTags.Length < capacity)
            {
                RemovedTags = new GameplayTag[capacity];
            }

            return RemovedTags;
        }
    }
}
