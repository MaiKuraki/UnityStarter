using System;
using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayTags.Core;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Owns pending replicated state, scratch buffers, and state-version counters for ASC delta generation.
    /// </summary>
    internal sealed class ReplicationStateBuilder
    {
        internal readonly struct MutationScope : IDisposable
        {
            private readonly ReplicationStateBuilder owner;
            private readonly int expectedDepth;

            internal MutationScope(ReplicationStateBuilder owner, int expectedDepth)
            {
                this.owner = owner;
                this.expectedDepth = expectedDepth;
            }

            public void Dispose()
            {
                owner?.EndMutationScope(expectedDepth);
            }
        }

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
        public readonly List<int> PendingRemovedEffectReconciliationIds = new List<int>(8);
        public readonly List<int> PendingRemovedAbilitySpecHandles = new List<int>(8);

        public GameplayTag[] EffectReplicationSetByCallerTags = Array.Empty<GameplayTag>();
        public long[] EffectReplicationSetByCallerValuesRaw = Array.Empty<long>();
        public GameplayTag[] StateApplySetByCallerTags = Array.Empty<GameplayTag>();
        public long[] StateApplySetByCallerValuesRaw = Array.Empty<long>();

        private int mutationScopeDepth;
        private bool attributeStructureReserved;

        internal int MutationScopeDepth => mutationScopeDepth;

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
            EnsureListCapacity(PendingRemovedEffectReconciliationIds, removedEffectCapacity);
            EnsureListCapacity(PendingRemovedAbilitySpecHandles, removedAbilityCapacity);
            EnsureHashSetCapacity(DirtyAttributeNames, dirtyAttributeCapacity);
            EnsureHashSetCapacity(PendingAddedTags, tagDeltaCapacity);
            EnsureHashSetCapacity(PendingRemovedTags, tagDeltaCapacity);
        }

        public void ResetAll()
        {
            if (mutationScopeDepth != 0)
            {
                throw new InvalidOperationException("Replication state cannot be reset while a mutation scope is active.");
            }

            ClearPendingStateChanges();
            StateVersion = 0UL;
            LastReplicatedStateVersion = 0UL;
            OutgoingDeltaSequence = 0U;
            AttributeRegistryVersion = 0U;
        }

        internal MutationScope BeginMutationScope(bool attributeStructure = false)
        {
            if (mutationScopeDepth == 0)
            {
                if (attributeStructure)
                {
                    EnsureAttributeStructureVersionAvailable();
                }
                else
                {
                    EnsureStateVersionAvailable();
                }

                IncrementStateVersionAfterPrecheck();
                if (attributeStructure)
                {
                    AttributeRegistryVersion++;
                    attributeStructureReserved = true;
                }
            }
            else if (attributeStructure && !attributeStructureReserved)
            {
                EnsureAttributeRegistryVersionAvailable();
                AttributeRegistryVersion++;
                attributeStructureReserved = true;
            }

            mutationScopeDepth++;
            return new MutationScope(this, mutationScopeDepth);
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
            PendingRemovedEffectReconciliationIds.Clear();
            PendingRemovedAbilitySpecHandles.Clear();
        }

        public void BeginCapture(GASAbilitySystemStateDeltaBuffer buffer)
        {
            if (buffer == null)
            {
                return;
            }

            buffer.ClearCounts();
            buffer.BaseVersion = LastReplicatedStateVersion;
            buffer.Sequence = NextOutgoingDeltaSequence();
        }

        public void FinalizeCapture(GASAbilitySystemStateDeltaBuffer buffer, ulong stateChecksum)
        {
            if (buffer == null)
            {
                return;
            }

            buffer.CurrentVersion = StateVersion;
            buffer.StateChecksum = stateChecksum;
        }

        public bool CommitCapture(GASAbilitySystemStateDeltaBuffer buffer)
        {
            if (buffer == null ||
                buffer.SchemaVersion != GASRuntimeDataContract.ReconciliationSchemaVersion ||
                buffer.Sequence == 0u ||
                buffer.StateChecksum == 0UL ||
                buffer.BaseVersion != LastReplicatedStateVersion ||
                buffer.CurrentVersion != StateVersion)
            {
                return false;
            }

            ClearPendingStateChanges();
            LastReplicatedStateVersion = buffer.CurrentVersion;
            return true;
        }

        public void CompleteCapture(GASAbilitySystemStateDeltaBuffer buffer, ulong stateChecksum)
        {
            FinalizeCapture(buffer, stateChecksum);
            if (buffer != null && !CommitCapture(buffer))
            {
                throw new InvalidOperationException("State changed while the delta capture was being completed.");
            }
        }

        public uint NextOutgoingDeltaSequence()
        {
            unchecked
            {
                OutgoingDeltaSequence++;
                if (OutgoingDeltaSequence == 0u)
                {
                    OutgoingDeltaSequence++;
                }
            }

            return OutgoingDeltaSequence;
        }

        public void MarkGrantedAbilitiesDirty()
        {
            if (mutationScopeDepth == 0)
            {
                EnsureStateVersionAvailable();
            }
            GrantedAbilitiesDirty = true;
            if (mutationScopeDepth == 0)
            {
                IncrementStateVersionAfterPrecheck();
            }
        }

        public void MarkActiveEffectsDirty()
        {
            if (mutationScopeDepth == 0)
            {
                EnsureStateVersionAvailable();
            }
            ActiveEffectsDirty = true;
            if (mutationScopeDepth == 0)
            {
                IncrementStateVersionAfterPrecheck();
            }
        }

        public void MarkAttributeValueDirty(GameplayAttribute attribute)
        {
            if (attribute == null || string.IsNullOrEmpty(attribute.Name))
            {
                return;
            }

            if (mutationScopeDepth == 0)
            {
                EnsureStateVersionAvailable();
            }
            if (DirtyAttributeNames.Add(attribute.Name))
            {
                DirtyAttributeValueSnapshots.Add(attribute);
            }

            if (mutationScopeDepth == 0)
            {
                IncrementStateVersionAfterPrecheck();
            }
        }

        public void MarkAttributeStructureDirty()
        {
            if (mutationScopeDepth == 0)
            {
                EnsureAttributeStructureVersionAvailable();
                AttributeRegistryVersion++;
            }
            else if (!attributeStructureReserved)
            {
                throw new InvalidOperationException(
                    "Attribute structure changed inside a replication mutation scope that did not reserve an attribute registry revision.");
            }

            AttributeStructureDirty = true;
            if (mutationScopeDepth == 0)
            {
                IncrementStateVersionAfterPrecheck();
            }
        }

        public bool TrackTagCountChange(GameplayTag tag, int newCount)
        {
            if (!tag.IsValid || tag.IsNone)
            {
                return false;
            }

            if (mutationScopeDepth == 0)
            {
                EnsureStateVersionAvailable();
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

            if (mutationScopeDepth == 0)
            {
                IncrementStateVersionAfterPrecheck();
            }
            return true;
        }

        public void TrackRemovedEffectReconciliationId(int reconciliationId)
        {
            if (reconciliationId == 0)
            {
                return;
            }

            PendingRemovedEffectReconciliationIds.Add(reconciliationId);
        }

        public void TrackRemovedAbilitySpecHandle(int specHandle)
        {
            if (specHandle <= 0)
            {
                return;
            }

            PendingRemovedAbilitySpecHandles.Add(specHandle);
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

        private void IncrementStateVersionAfterPrecheck()
        {
            StateVersion++;
        }

        internal void EnsureStateVersionAvailable()
        {
            if (StateVersion == ulong.MaxValue)
            {
                throw new InvalidOperationException(
                    "The replication state version is exhausted. Start a new replication stream and full baseline instead of wrapping the version.");
            }
        }

        internal void EnsureAttributeStructureVersionAvailable()
        {
            EnsureStateVersionAvailable();
            EnsureAttributeRegistryVersionAvailable();
        }

        private void EnsureAttributeRegistryVersionAvailable()
        {
            if (AttributeRegistryVersion == uint.MaxValue)
            {
                throw new InvalidOperationException(
                    "The attribute registry revision is exhausted. Recreate the replication stream instead of wrapping the revision.");
            }
        }

        private void EndMutationScope(int expectedDepth)
        {
            if (expectedDepth <= 0 || mutationScopeDepth != expectedDepth)
            {
                throw new InvalidOperationException("Replication mutation scopes must be disposed exactly once in last-in-first-out order.");
            }

            mutationScopeDepth--;
            if (mutationScopeDepth == 0)
            {
                attributeStructureReserved = false;
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
