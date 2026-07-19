using System;
using System.Collections.Generic;
using System.Threading;

namespace CycloneGames.GameplayAbilities.Networking
{
    internal enum GASReplicaIdentityBindResult : byte
    {
        Invalid = 0,
        Bound = 1,
        Existing = 2,
        Conflict = 3,
        CapacityExhausted = 4
    }

    /// <summary>
    /// Owner-thread mapping from authority-issued identities to process-local replica identities.
    /// </summary>
    internal sealed class GASReplicaIdentityMap
    {
        private readonly int ownerThreadId;
        private readonly IdentityTable grants;
        private readonly IdentityTable effects;
        private uint streamEpoch;

        public GASReplicaIdentityMap(
            GASNetworkEntityId entity,
            uint streamEpoch,
            int grantCapacity,
            int effectCapacity)
        {
            if (!entity.IsValid)
                throw new ArgumentOutOfRangeException(nameof(entity));
            if (streamEpoch == 0u)
                throw new ArgumentOutOfRangeException(nameof(streamEpoch));
            ValidateCapacity(grantCapacity, nameof(grantCapacity));
            ValidateCapacity(effectCapacity, nameof(effectCapacity));

            Entity = entity;
            this.streamEpoch = streamEpoch;
            ownerThreadId = Thread.CurrentThread.ManagedThreadId;
            grants = new IdentityTable(grantCapacity);
            effects = new IdentityTable(effectCapacity);
        }

        public GASNetworkEntityId Entity { get; }
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

        public GASReplicaIdentityBindResult BindGrant(
            GASNetworkGrantId wireId,
            int abilitySpecHandle)
        {
            AssertOwnerThread();
            return grants.Bind(wireId.Value, abilitySpecHandle);
        }

        public GASReplicaIdentityBindResult BindEffect(
            GASNetworkEffectId wireId,
            int reconciliationId)
        {
            AssertOwnerThread();
            return effects.Bind(wireId.Value, reconciliationId);
        }

        public bool TryGetAbilitySpecHandle(
            GASNetworkGrantId wireId,
            out int abilitySpecHandle)
        {
            AssertOwnerThread();
            return grants.TryGetLocal(wireId.Value, out abilitySpecHandle);
        }

        public bool TryGetGrantId(
            int abilitySpecHandle,
            out GASNetworkGrantId wireId)
        {
            AssertOwnerThread();
            bool found = grants.TryGetWire(abilitySpecHandle, out ulong value);
            wireId = new GASNetworkGrantId(value);
            return found;
        }

        public bool TryGetEffectReconciliationId(
            GASNetworkEffectId wireId,
            out int reconciliationId)
        {
            AssertOwnerThread();
            return effects.TryGetLocal(wireId.Value, out reconciliationId);
        }

        public bool TryGetEffectId(
            int reconciliationId,
            out GASNetworkEffectId wireId)
        {
            AssertOwnerThread();
            bool found = effects.TryGetWire(reconciliationId, out ulong value);
            wireId = new GASNetworkEffectId(value);
            return found;
        }

        public bool RemoveGrant(GASNetworkGrantId wireId, out int abilitySpecHandle)
        {
            AssertOwnerThread();
            return grants.Remove(wireId.Value, out abilitySpecHandle);
        }

        public bool RemoveEffect(GASNetworkEffectId wireId, out int reconciliationId)
        {
            AssertOwnerThread();
            return effects.Remove(wireId.Value, out reconciliationId);
        }

        public void ResetEpoch(uint newStreamEpoch)
        {
            AssertOwnerThread();
            if (newStreamEpoch == 0u || newStreamEpoch == streamEpoch)
                throw new ArgumentOutOfRangeException(nameof(newStreamEpoch));

            grants.Clear();
            effects.Clear();
            streamEpoch = newStreamEpoch;
        }

        private void AssertOwnerThread()
        {
            int currentThreadId = Thread.CurrentThread.ManagedThreadId;
            if (currentThreadId != ownerThreadId)
            {
                throw new InvalidOperationException(
                    $"GAS replica identity map is owned by thread {ownerThreadId} and cannot be accessed from thread {currentThreadId}.");
            }
        }

        private static void ValidateCapacity(int capacity, string parameterName)
        {
            if (capacity < 0 || capacity > GASAuthorityIdentityMap.MaximumMappingCapacity)
                throw new ArgumentOutOfRangeException(parameterName);
        }

        private sealed class IdentityTable
        {
            private readonly int capacity;
            private readonly Dictionary<ulong, int> localByWire;
            private readonly Dictionary<int, ulong> wireByLocal;

            public IdentityTable(int capacity)
            {
                this.capacity = capacity;
                localByWire = new Dictionary<ulong, int>(capacity);
                wireByLocal = new Dictionary<int, ulong>(capacity);
            }

            public int Count => localByWire.Count;

            public GASReplicaIdentityBindResult Bind(ulong wireId, int localId)
            {
                if (wireId == 0UL || localId <= 0)
                    return GASReplicaIdentityBindResult.Invalid;

                bool hasWire = localByWire.TryGetValue(wireId, out int existingLocal);
                bool hasLocal = wireByLocal.TryGetValue(localId, out ulong existingWire);
                if (hasWire || hasLocal)
                {
                    return hasWire && hasLocal && existingLocal == localId && existingWire == wireId
                        ? GASReplicaIdentityBindResult.Existing
                        : GASReplicaIdentityBindResult.Conflict;
                }

                if (localByWire.Count >= capacity)
                    return GASReplicaIdentityBindResult.CapacityExhausted;

                localByWire.Add(wireId, localId);
                try
                {
                    wireByLocal.Add(localId, wireId);
                }
                catch
                {
                    localByWire.Remove(wireId);
                    throw;
                }

                return GASReplicaIdentityBindResult.Bound;
            }

            public bool TryGetLocal(ulong wireId, out int localId)
            {
                if (wireId != 0UL)
                    return localByWire.TryGetValue(wireId, out localId);
                localId = 0;
                return false;
            }

            public bool TryGetWire(int localId, out ulong wireId)
            {
                if (localId > 0)
                    return wireByLocal.TryGetValue(localId, out wireId);
                wireId = 0UL;
                return false;
            }

            public bool Remove(ulong wireId, out int localId)
            {
                localId = 0;
                if (wireId == 0UL || !localByWire.TryGetValue(wireId, out localId))
                    return false;

                if (!localByWire.Remove(wireId) || !wireByLocal.Remove(localId))
                    throw new InvalidOperationException("The GAS replica identity indexes are inconsistent.");
                return true;
            }

            public void Clear()
            {
                localByWire.Clear();
                wireByLocal.Clear();
            }
        }
    }
}
