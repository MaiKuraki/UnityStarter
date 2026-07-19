using System;
using System.Collections.Generic;
using System.Threading;

namespace CycloneGames.GameplayAbilities.Networking
{
    /// <summary>Result of resolving or allocating one authority-issued identity.</summary>
    public enum GASAuthorityIdentityMapResult : byte
    {
        Invalid = 0,
        Created = 1,
        Existing = 2,
        InvalidLocalIdentity = 3,
        CapacityExhausted = 4,
        IdentityExhausted = 5
    }

    /// <summary>
    /// Owner-thread mapping between one entity's process-local GAS identities and authority-issued
    /// wire identities for a single stream epoch.
    /// </summary>
    /// <remarks>
    /// The configured capacities are hard limits. Dictionaries are pre-sized during construction,
    /// removed wire IDs are not reused within an epoch, and no locking is performed.
    /// </remarks>
    public sealed class GASAuthorityIdentityMap
    {
        public const int MaximumMappingCapacity = 1_048_576;

        private readonly GASNetworkEntityId entity;
        private readonly int ownerThreadId;
        private readonly IdentityTable grants;
        private readonly IdentityTable effects;
        private uint streamEpoch;

        public GASAuthorityIdentityMap(
            GASNetworkEntityId entity,
            uint streamEpoch,
            int grantCapacity,
            int effectCapacity)
            : this(
                entity,
                streamEpoch,
                grantCapacity,
                effectCapacity,
                firstGrantId: 1UL,
                firstEffectId: 1UL)
        {
        }

        internal GASAuthorityIdentityMap(
            GASNetworkEntityId entity,
            uint streamEpoch,
            int grantCapacity,
            int effectCapacity,
            ulong firstGrantId,
            ulong firstEffectId)
        {
            if (!entity.IsValid)
                throw new ArgumentOutOfRangeException(nameof(entity));
            if (streamEpoch == 0u)
                throw new ArgumentOutOfRangeException(nameof(streamEpoch));
            ValidateCapacity(grantCapacity, nameof(grantCapacity));
            ValidateCapacity(effectCapacity, nameof(effectCapacity));
            if (firstGrantId == 0UL)
                throw new ArgumentOutOfRangeException(nameof(firstGrantId));
            if (firstEffectId == 0UL)
                throw new ArgumentOutOfRangeException(nameof(firstEffectId));

            this.entity = entity;
            this.streamEpoch = streamEpoch;
            ownerThreadId = Thread.CurrentThread.ManagedThreadId;
            grants = new IdentityTable(grantCapacity, firstGrantId);
            effects = new IdentityTable(effectCapacity, firstEffectId);
        }

        public GASNetworkEntityId Entity => entity;
        public int OwnerThreadId => ownerThreadId;
        public bool IsOwnerThread => Thread.CurrentThread.ManagedThreadId == ownerThreadId;
        public int GrantCapacity => grants.Capacity;
        public int EffectCapacity => effects.Capacity;

        public uint StreamEpoch
        {
            get
            {
                AssertOwnerThread();
                return streamEpoch;
            }
        }

        public int GrantCount
        {
            get
            {
                AssertOwnerThread();
                return grants.Count;
            }
        }

        public int EffectCount
        {
            get
            {
                AssertOwnerThread();
                return effects.Count;
            }
        }

        public GASAuthorityIdentityMapResult GetOrCreateGrantId(
            int abilitySpecHandle,
            out GASNetworkGrantId grantId)
        {
            AssertOwnerThread();
            GASAuthorityIdentityMapResult result = grants.GetOrCreate(abilitySpecHandle, out ulong value);
            grantId = new GASNetworkGrantId(value);
            return result;
        }

        public bool TryGetGrantId(int abilitySpecHandle, out GASNetworkGrantId grantId)
        {
            AssertOwnerThread();
            bool found = grants.TryGetWireId(abilitySpecHandle, out ulong value);
            grantId = new GASNetworkGrantId(value);
            return found;
        }

        public bool TryGetAbilitySpecHandle(GASNetworkGrantId grantId, out int abilitySpecHandle)
        {
            AssertOwnerThread();
            return grants.TryGetLocalId(grantId.Value, out abilitySpecHandle);
        }

        public bool RemoveGrantBySpecHandle(int abilitySpecHandle, out GASNetworkGrantId removedGrantId)
        {
            AssertOwnerThread();
            bool removed = grants.RemoveByLocalId(abilitySpecHandle, out ulong value);
            removedGrantId = new GASNetworkGrantId(value);
            return removed;
        }

        public GASAuthorityIdentityMapResult GetOrCreateEffectId(
            int effectReconciliationId,
            out GASNetworkEffectId effectId)
        {
            AssertOwnerThread();
            GASAuthorityIdentityMapResult result = effects.GetOrCreate(effectReconciliationId, out ulong value);
            effectId = new GASNetworkEffectId(value);
            return result;
        }

        public bool TryGetEffectId(int effectReconciliationId, out GASNetworkEffectId effectId)
        {
            AssertOwnerThread();
            bool found = effects.TryGetWireId(effectReconciliationId, out ulong value);
            effectId = new GASNetworkEffectId(value);
            return found;
        }

        public bool TryGetEffectReconciliationId(
            GASNetworkEffectId effectId,
            out int effectReconciliationId)
        {
            AssertOwnerThread();
            return effects.TryGetLocalId(effectId.Value, out effectReconciliationId);
        }

        public bool RemoveEffectByReconciliationId(
            int effectReconciliationId,
            out GASNetworkEffectId removedEffectId)
        {
            AssertOwnerThread();
            bool removed = effects.RemoveByLocalId(effectReconciliationId, out ulong value);
            removedEffectId = new GASNetworkEffectId(value);
            return removed;
        }

        /// <summary>
        /// Starts a new stream epoch, clears every mapping, and resets the per-epoch identity sequences.
        /// </summary>
        public void ResetEpoch(uint newStreamEpoch)
        {
            AssertOwnerThread();
            if (newStreamEpoch == 0u || newStreamEpoch == streamEpoch)
                throw new ArgumentOutOfRangeException(nameof(newStreamEpoch));

            grants.Reset();
            effects.Reset();
            streamEpoch = newStreamEpoch;
        }

        private void AssertOwnerThread()
        {
            if (!IsOwnerThread)
            {
                throw new InvalidOperationException(
                    $"GAS authority identity map is owned by thread {ownerThreadId} and cannot be accessed from thread {Thread.CurrentThread.ManagedThreadId}.");
            }
        }

        private static void ValidateCapacity(int capacity, string parameterName)
        {
            if (capacity < 0 || capacity > MaximumMappingCapacity)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    capacity,
                    $"Mapping capacity must be between 0 and {MaximumMappingCapacity}.");
            }
        }

        private sealed class IdentityTable
        {
            private readonly int capacity;
            private readonly Dictionary<int, ulong> wireIdByLocalId;
            private readonly Dictionary<ulong, int> localIdByWireId;
            private ulong nextWireId;

            public IdentityTable(int capacity, ulong firstWireId)
            {
                this.capacity = capacity;
                nextWireId = firstWireId;
                wireIdByLocalId = new Dictionary<int, ulong>(capacity);
                localIdByWireId = new Dictionary<ulong, int>(capacity);
            }

            public int Capacity => capacity;
            public int Count => wireIdByLocalId.Count;

            public GASAuthorityIdentityMapResult GetOrCreate(int localId, out ulong wireId)
            {
                wireId = 0UL;
                if (localId <= 0)
                    return GASAuthorityIdentityMapResult.InvalidLocalIdentity;
                if (wireIdByLocalId.TryGetValue(localId, out wireId))
                    return GASAuthorityIdentityMapResult.Existing;
                if (wireIdByLocalId.Count >= capacity)
                {
                    wireId = 0UL;
                    return GASAuthorityIdentityMapResult.CapacityExhausted;
                }
                if (nextWireId == 0UL)
                    return GASAuthorityIdentityMapResult.IdentityExhausted;

                ulong allocatedWireId = nextWireId;
                wireIdByLocalId.Add(localId, allocatedWireId);
                try
                {
                    localIdByWireId.Add(allocatedWireId, localId);
                }
                catch
                {
                    wireIdByLocalId.Remove(localId);
                    throw;
                }

                nextWireId = allocatedWireId == ulong.MaxValue ? 0UL : allocatedWireId + 1UL;
                wireId = allocatedWireId;
                return GASAuthorityIdentityMapResult.Created;
            }

            public bool TryGetWireId(int localId, out ulong wireId)
            {
                if (localId <= 0)
                {
                    wireId = 0UL;
                    return false;
                }

                return wireIdByLocalId.TryGetValue(localId, out wireId);
            }

            public bool TryGetLocalId(ulong wireId, out int localId)
            {
                if (wireId == 0UL)
                {
                    localId = 0;
                    return false;
                }

                return localIdByWireId.TryGetValue(wireId, out localId);
            }

            public bool RemoveByLocalId(int localId, out ulong removedWireId)
            {
                removedWireId = 0UL;
                if (localId <= 0 || !wireIdByLocalId.TryGetValue(localId, out ulong wireId))
                    return false;

                if (!wireIdByLocalId.Remove(localId))
                    throw new InvalidOperationException("The GAS authority identity forward index is inconsistent.");
                if (!localIdByWireId.Remove(wireId))
                {
                    wireIdByLocalId.Add(localId, wireId);
                    throw new InvalidOperationException("The GAS authority identity reverse index is inconsistent.");
                }

                removedWireId = wireId;
                return true;
            }

            public void Reset()
            {
                wireIdByLocalId.Clear();
                localIdByWireId.Clear();
                nextWireId = 1UL;
            }
        }
    }
}
