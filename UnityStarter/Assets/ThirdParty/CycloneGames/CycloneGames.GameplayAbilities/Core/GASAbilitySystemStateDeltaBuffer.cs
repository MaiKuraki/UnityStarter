using System;
using CycloneGames.GameplayTags.Core;

namespace CycloneGames.GameplayAbilities.Core
{
    /// <summary>
    /// Reusable process-local scratch buffer for incremental authority-to-replica reconciliation.
    /// It contains runtime object references and local IDs, so it is not a wire DTO, an asynchronous
    /// message, or a production transport protocol.
    ///
    /// The caller owns the buffer. Each public array has a companion Count field; only indices
    /// [0, Count) contain valid data. Reserve pre-allocates internal arrays for capture, and
    /// ClearCounts resets the counted ranges before reuse.
    /// </summary>
    public sealed class GASAbilitySystemStateDeltaBuffer
    {
        public ushort SchemaVersion = GASRuntimeDataContract.ReconciliationSchemaVersion;
        public uint Sequence;
        public ulong StateChecksum;
        public ulong BaseVersion;
        public ulong CurrentVersion;
        public AbilitySystemStateChangeMask ChangeMask;

        public GASGrantedAbilityStateData[] GrantedAbilities = Array.Empty<GASGrantedAbilityStateData>();
        public int GrantedAbilityCount;

        public int[] RemovedAbilitySpecHandles = Array.Empty<int>();
        public int RemovedAbilitySpecHandleCount;

        public GASActiveEffectStateData[] ActiveEffects = Array.Empty<GASActiveEffectStateData>();
        public int ActiveEffectCount;
        public GASSetByCallerTagStateData[][] ActiveEffectSetByCallerMagnitudes = Array.Empty<GASSetByCallerTagStateData[]>();
        public GASSetByCallerNameStateData[][] ActiveEffectSetByCallerNameMagnitudes = Array.Empty<GASSetByCallerNameStateData[]>();
        public GameplayTag[][] ActiveEffectDynamicGrantedTags = Array.Empty<GameplayTag[]>();
        public GameplayTag[][] ActiveEffectDynamicAssetTags = Array.Empty<GameplayTag[]>();

        public int[] RemovedEffectReconciliationIds = Array.Empty<int>();
        public int RemovedEffectReconciliationIdCount;

        public GASAttributeStateData[] Attributes = Array.Empty<GASAttributeStateData>();
        public int AttributeCount;

        public GameplayTag[] AddedTags = Array.Empty<GameplayTag>();
        public int AddedTagCount;

        public GameplayTag[] RemovedTags = Array.Empty<GameplayTag>();
        public int RemovedTagCount;

        public bool HasChanges => ChangeMask != AbilitySystemStateChangeMask.None;

        public void Reserve(
            int grantedAbilityCapacity,
            int removedAbilitySpecCapacity,
            int activeEffectCapacity,
            int removedEffectCapacity,
            int attributeCapacity,
            int addedTagCapacity,
            int removedTagCapacity,
            int maxSetByCallerPerEffect = 0,
            int maxSetByCallerNamesPerEffect = 0,
            int maxDynamicGrantedTagsPerEffect = 0,
            int maxDynamicAssetTagsPerEffect = 0)
        {
            EnsureGrantedAbilityCapacity(grantedAbilityCapacity);
            EnsureRemovedAbilitySpecHandleCapacity(removedAbilitySpecCapacity);
            EnsureActiveEffectCapacity(activeEffectCapacity);
            EnsureRemovedEffectReconciliationIdCapacity(removedEffectCapacity);
            EnsureAttributeCapacity(attributeCapacity);
            EnsureAddedTagCapacity(addedTagCapacity);
            EnsureRemovedTagCapacity(removedTagCapacity);

            if (maxSetByCallerPerEffect > 0 ||
                maxSetByCallerNamesPerEffect > 0 ||
                maxDynamicGrantedTagsPerEffect > 0 ||
                maxDynamicAssetTagsPerEffect > 0)
            {
                for (int i = 0; i < activeEffectCapacity; i++)
                {
                    EnsureActiveEffectSetByCallerCapacity(i, maxSetByCallerPerEffect);
                    EnsureActiveEffectSetByCallerNameCapacity(i, maxSetByCallerNamesPerEffect);
                    EnsureActiveEffectDynamicGrantedTagCapacity(i, maxDynamicGrantedTagsPerEffect);
                    EnsureActiveEffectDynamicAssetTagCapacity(i, maxDynamicAssetTagsPerEffect);
                }
            }
        }

        public void ClearCounts()
        {
            SchemaVersion = GASRuntimeDataContract.ReconciliationSchemaVersion;
            Sequence = 0;
            StateChecksum = 0;
            BaseVersion = 0;
            CurrentVersion = 0;
            ChangeMask = AbilitySystemStateChangeMask.None;
            GrantedAbilityCount = 0;
            RemovedAbilitySpecHandleCount = 0;
            ActiveEffectCount = 0;
            RemovedEffectReconciliationIdCount = 0;
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

        public int[] EnsureRemovedAbilitySpecHandleCapacity(int capacity)
        {
            if (RemovedAbilitySpecHandles.Length < capacity)
            {
                RemovedAbilitySpecHandles = new int[capacity];
            }

            return RemovedAbilitySpecHandles;
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

            ResizeJagged(ref ActiveEffectSetByCallerNameMagnitudes, capacity);
            ResizeJagged(ref ActiveEffectDynamicGrantedTags, capacity);
            ResizeJagged(ref ActiveEffectDynamicAssetTags, capacity);

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

        public GASSetByCallerNameStateData[] EnsureActiveEffectSetByCallerNameCapacity(int effectIndex, int capacity)
        {
            if (effectIndex < 0) return Array.Empty<GASSetByCallerNameStateData>();
            EnsureActiveEffectCapacity(effectIndex + 1);
            var entries = ActiveEffectSetByCallerNameMagnitudes[effectIndex];
            if (entries == null || entries.Length < capacity)
            {
                entries = new GASSetByCallerNameStateData[capacity];
                ActiveEffectSetByCallerNameMagnitudes[effectIndex] = entries;
            }
            return entries;
        }

        public GameplayTag[] EnsureActiveEffectDynamicGrantedTagCapacity(int effectIndex, int capacity)
        {
            if (effectIndex < 0) return Array.Empty<GameplayTag>();
            EnsureActiveEffectCapacity(effectIndex + 1);
            var entries = ActiveEffectDynamicGrantedTags[effectIndex];
            if (entries == null || entries.Length < capacity)
            {
                entries = new GameplayTag[capacity];
                ActiveEffectDynamicGrantedTags[effectIndex] = entries;
            }
            return entries;
        }

        public GameplayTag[] EnsureActiveEffectDynamicAssetTagCapacity(int effectIndex, int capacity)
        {
            if (effectIndex < 0) return Array.Empty<GameplayTag>();
            EnsureActiveEffectCapacity(effectIndex + 1);
            var entries = ActiveEffectDynamicAssetTags[effectIndex];
            if (entries == null || entries.Length < capacity)
            {
                entries = new GameplayTag[capacity];
                ActiveEffectDynamicAssetTags[effectIndex] = entries;
            }
            return entries;
        }

        private static void ResizeJagged<T>(ref T[][] values, int capacity)
        {
            if (values.Length >= capacity)
            {
                return;
            }

            T[][] existing = values;
            values = new T[capacity][];
            for (int i = 0; i < existing.Length; i++)
            {
                values[i] = existing[i];
            }
        }

        public int[] EnsureRemovedEffectReconciliationIdCapacity(int capacity)
        {
            if (RemovedEffectReconciliationIds.Length < capacity)
            {
                RemovedEffectReconciliationIds = new int[capacity];
            }

            return RemovedEffectReconciliationIds;
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
