using System;
using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayTags.Core;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Owns pending replicated state, scratch buffers, and state-version counters for ASC delta generation.
    /// </summary>
    public sealed class ReplicationStateBuilder
    {
        public ulong StateVersion;
        public ulong LastReplicatedStateVersion;
        public uint OutgoingDeltaSequence;
        public uint AttributeRegistryVersion;
        public bool GrantedAbilitiesDirty;
        public bool ActiveEffectsDirty;
        public bool AttributeStructureDirty;
        public bool TagsDirty;

        public readonly HashSet<string> DirtyAttributeNames = new HashSet<string>(StringComparer.Ordinal);
        public readonly List<GameplayAttribute> DirtyAttributeValueSnapshots = new List<GameplayAttribute>(32);
        public readonly HashSet<GameplayTag> PendingAddedTags = new HashSet<GameplayTag>();
        public readonly HashSet<GameplayTag> PendingRemovedTags = new HashSet<GameplayTag>();
        public readonly List<GameplayTag> PendingAddedTagSnapshots = new List<GameplayTag>(8);
        public readonly List<GameplayTag> PendingRemovedTagSnapshots = new List<GameplayTag>(8);
        public readonly List<int> PendingRemovedEffectNetIds = new List<int>(8);
        public readonly List<IGASAbilityDefinition> PendingRemovedAbilityDefs = new List<IGASAbilityDefinition>(8);

        public GameplayTag[] EffectReplicationSetByCallerTags = Array.Empty<GameplayTag>();
        public long[] EffectReplicationSetByCallerValuesRaw = Array.Empty<long>();
        public GameplayTag[] StateApplySetByCallerTags = Array.Empty<GameplayTag>();
        public long[] StateApplySetByCallerValuesRaw = Array.Empty<long>();
        public int[] TargetDataNetworkIdBuffer = Array.Empty<int>();

        public AbilitySystemStateChangeMask PendingMask
        {
            get
            {
                var mask = AbilitySystemStateChangeMask.None;
                if (GrantedAbilitiesDirty)
                {
                    mask |= AbilitySystemStateChangeMask.GrantedAbilities;
                }

                if (ActiveEffectsDirty)
                {
                    mask |= AbilitySystemStateChangeMask.ActiveEffects;
                }

                if (AttributeStructureDirty || DirtyAttributeNames.Count > 0)
                {
                    mask |= AbilitySystemStateChangeMask.Attributes;
                }

                if (TagsDirty)
                {
                    mask |= AbilitySystemStateChangeMask.Tags;
                }

                return mask;
            }
        }

        public void Reserve(
            int dirtyAttributeCapacity,
            int tagDeltaCapacity,
            int removedEffectCapacity,
            int removedAbilityCapacity)
        {
            EnsureListCapacity(DirtyAttributeValueSnapshots, dirtyAttributeCapacity);
            EnsureListCapacity(PendingAddedTagSnapshots, tagDeltaCapacity);
            EnsureListCapacity(PendingRemovedTagSnapshots, tagDeltaCapacity);
            EnsureListCapacity(PendingRemovedEffectNetIds, removedEffectCapacity);
            EnsureListCapacity(PendingRemovedAbilityDefs, removedAbilityCapacity);
            EnsureHashSetCapacity(DirtyAttributeNames, dirtyAttributeCapacity);
            EnsureHashSetCapacity(PendingAddedTags, tagDeltaCapacity);
            EnsureHashSetCapacity(PendingRemovedTags, tagDeltaCapacity);
        }

        public void ResetAll()
        {
            ClearPendingStateChanges();
            StateVersion = 0UL;
            LastReplicatedStateVersion = 0UL;
            OutgoingDeltaSequence = 0U;
            AttributeRegistryVersion = 0U;
        }

        public void ClearPendingStateChanges()
        {
            GrantedAbilitiesDirty = false;
            ActiveEffectsDirty = false;
            AttributeStructureDirty = false;
            TagsDirty = false;
            DirtyAttributeNames.Clear();
            DirtyAttributeValueSnapshots.Clear();
            PendingAddedTags.Clear();
            PendingRemovedTags.Clear();
            PendingAddedTagSnapshots.Clear();
            PendingRemovedTagSnapshots.Clear();
            PendingRemovedEffectNetIds.Clear();
            PendingRemovedAbilityDefs.Clear();
        }

        public void BeginCapture(GASAbilitySystemStateDeltaBuffer buffer)
        {
            if (buffer == null)
            {
                return;
            }

            buffer.ClearCounts();
            buffer.BaseVersion = LastReplicatedStateVersion;
        }

        public void CompleteCapture(GASAbilitySystemStateDeltaBuffer buffer, ulong stateChecksum)
        {
            if (buffer == null)
            {
                return;
            }

            ClearPendingStateChanges();
            buffer.CurrentVersion = StateVersion;
            buffer.StateChecksum = stateChecksum;
            LastReplicatedStateVersion = StateVersion;
        }

        public uint NextOutgoingDeltaSequence()
        {
            unchecked
            {
                OutgoingDeltaSequence++;
            }

            return OutgoingDeltaSequence;
        }

        public void MarkGrantedAbilitiesDirty()
        {
            GrantedAbilitiesDirty = true;
            IncrementStateVersion();
        }

        public void MarkActiveEffectsDirty()
        {
            ActiveEffectsDirty = true;
            IncrementStateVersion();
        }

        public void MarkAttributeValueDirty(GameplayAttribute attribute)
        {
            if (attribute == null || string.IsNullOrEmpty(attribute.Name))
            {
                return;
            }

            if (DirtyAttributeNames.Add(attribute.Name))
            {
                DirtyAttributeValueSnapshots.Add(attribute);
            }

            IncrementStateVersion();
        }

        public void MarkAttributeStructureDirty()
        {
            AttributeStructureDirty = true;
            unchecked
            {
                AttributeRegistryVersion++;
            }

            IncrementStateVersion();
        }

        public bool TrackTagCountChange(GameplayTag tag, int newCount)
        {
            if (!tag.IsValid || tag.IsNone)
            {
                return false;
            }

            TagsDirty = true;
            if (newCount > 0)
            {
                if (PendingRemovedTags.Remove(tag))
                {
                    RemoveTagSnapshot(PendingRemovedTagSnapshots, tag);
                }
                else if (PendingAddedTags.Add(tag))
                {
                    PendingAddedTagSnapshots.Add(tag);
                }
            }
            else
            {
                if (PendingAddedTags.Remove(tag))
                {
                    RemoveTagSnapshot(PendingAddedTagSnapshots, tag);
                }
                else if (PendingRemovedTags.Add(tag))
                {
                    PendingRemovedTagSnapshots.Add(tag);
                }
            }

            IncrementStateVersion();
            return true;
        }

        public void TrackRemovedEffectNetworkId(int networkId)
        {
            if (networkId == 0)
            {
                return;
            }

            PendingRemovedEffectNetIds.Add(networkId);
        }

        public void TrackRemovedAbilityDefinition(IGASAbilityDefinition abilityDefinition)
        {
            if (abilityDefinition == null)
            {
                return;
            }

            PendingRemovedAbilityDefs.Add(abilityDefinition);
        }

        public void EnsureEffectReplicationSetByCallerCapacity(int count)
        {
            EnsureArrayCapacity(ref EffectReplicationSetByCallerTags, count);
            EnsureArrayCapacity(ref EffectReplicationSetByCallerValuesRaw, count);
        }

        public void EnsureStateApplySetByCallerCapacity(int count)
        {
            EnsureArrayCapacity(ref StateApplySetByCallerTags, count);
            EnsureArrayCapacity(ref StateApplySetByCallerValuesRaw, count);
        }

        public void EnsureTargetDataNetworkIdCapacity(int count)
        {
            EnsureArrayCapacity(ref TargetDataNetworkIdBuffer, count);
        }

        private void IncrementStateVersion()
        {
            unchecked
            {
                StateVersion++;
            }
        }

        private static void RemoveTagSnapshot(List<GameplayTag> tags, GameplayTag tag)
        {
            for (int i = tags.Count - 1; i >= 0; i--)
            {
                if (tags[i].Equals(tag))
                {
                    int lastIndex = tags.Count - 1;
                    tags[i] = tags[lastIndex];
                    tags.RemoveAt(lastIndex);
                    return;
                }
            }
        }

        private static void EnsureArrayCapacity<T>(ref T[] buffer, int count)
        {
            if (count <= 0 || buffer.Length >= count)
            {
                return;
            }

            int next = Math.Max(count, buffer.Length == 0 ? 4 : buffer.Length * 2);
            Array.Resize(ref buffer, next);
        }

        private static void EnsureListCapacity<T>(List<T> list, int capacity)
        {
            if (list != null && capacity > list.Capacity)
            {
                list.Capacity = capacity;
            }
        }

        private static void EnsureHashSetCapacity<T>(HashSet<T> set, int capacity)
        {
            if (set != null && capacity > 0)
            {
                set.EnsureCapacity(capacity);
            }
        }
    }
}
