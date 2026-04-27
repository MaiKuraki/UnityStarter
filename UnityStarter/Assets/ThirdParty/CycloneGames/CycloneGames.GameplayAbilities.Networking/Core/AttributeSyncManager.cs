using System;
using System.Collections.Generic;
using CycloneGames.Networking;

namespace CycloneGames.GameplayAbilities.Networking
{
    /// <summary>
    /// Server-side attribute dirty tracking and batched sync.
    /// 
    /// Usage:
    /// 1. Game code modifies attributes on the ASC
    /// 2. Call MarkDirty(attributeId) or auto-detect via hooks
    /// 3. Each server tick, call FlushDirtyToObservers() to send only changed attributes
    /// 4. On new client join, call SendFullSync() for complete state
    /// 
    /// Supports ReplicationMode:
    /// - Full: all attribute changes replicated to all observers
    /// - Mixed: owner gets full, others get gameplay-relevant subset
    /// - Minimal: owner gets full, others get nothing (server-only attributes)
    /// </summary>
    public sealed class AttributeSyncManager
    {
        private readonly NetworkedAbilityBridge _bridge;

        // Per-ASC dirty tracking
        private readonly Dictionary<uint, DirtyAttributeSet> _dirtyMap =
            new Dictionary<uint, DirtyAttributeSet>(64);

        // Replication filter: which attributes are replicated to non-owners
        // attributeId ->true means replicate to observers (not just owner)
        private readonly HashSet<int> _publicAttributes = new HashSet<int>();
        private AttributeEntry[] _fullSyncEntries = Array.Empty<AttributeEntry>();
        private int _defaultAttributeCapacity = 16;

        public AttributeSyncManager(NetworkedAbilityBridge bridge)
        {
            _bridge = bridge;
        }

        public void ReserveRuntimeCapacity(int entityCapacity = 64, int attributeCapacity = 32)
        {
            _defaultAttributeCapacity = Math.Max(1, attributeCapacity);

            if (attributeCapacity > _fullSyncEntries.Length)
            {
                _fullSyncEntries = new AttributeEntry[attributeCapacity];
            }
        }

        /// <summary>
        /// Mark an attribute as "public" --replicated to all observers.
        /// By default, attributes are only sent to the owning client.
        /// Public examples: Health, Mana (visible on health bars).
        /// Private examples: Gold, XP, internal cooldown values.
        /// </summary>
        public void RegisterPublicAttribute(int attributeId) => _publicAttributes.Add(attributeId);

        /// <summary>
        /// Remove all dirty tracking for an entity (call on despawn/destroy).
        /// </summary>
        public void ClearEntity(uint networkId) => _dirtyMap.Remove(networkId);

        /// <summary>
        /// Mark an attribute as dirty for a specific ASC.
        /// Call this from the AttributeSet's PostGameplayEffectExecute or PreAttributeChange hooks.
        /// </summary>
        public void MarkDirty(uint networkId, int attributeId, float baseValue, float currentValue)
        {
            if (!_dirtyMap.TryGetValue(networkId, out var set))
            {
                set = new DirtyAttributeSet(_defaultAttributeCapacity);
                _dirtyMap[networkId] = set;
            }

            set.SetDirty(attributeId, baseValue, currentValue);
        }

        /// <summary>
        /// Flush all dirty attributes to their respective observers.
        /// Call once per server tick.
        /// </summary>
        public void FlushDirty(Func<uint, int> getOwnerConnectionId,
            Func<uint, IReadOnlyList<INetConnection>> getObservers,
            Func<int, INetConnection> getConnectionById)
        {
            foreach (var pair in _dirtyMap)
            {
                var set = pair.Value;
                if (set.DirtyCount == 0) continue;

                uint networkId = pair.Key;
                int ownerConnId = getOwnerConnectionId(networkId);
                var observers = getObservers(networkId);

                // Owner gets all dirty attributes (Full or Mixed mode)
                var ownerConn = getConnectionById(ownerConnId);
                if (ownerConn != null)
                {
                    var ownerData = set.BuildUpdateData(networkId, includeAll: true);
                    _bridge.ServerSyncAttributes(ownerConn, networkId, ownerData);
                }

                // Other observers get only public attributes
                if (observers != null && observers.Count > 0)
                {
                    var publicData = set.BuildUpdateData(networkId, includeAll: false, _publicAttributes);
                    if (publicData.AttributeCount > 0)
                    {
                        for (int i = 0; i < observers.Count; i++)
                        {
                            if (observers[i].ConnectionId == ownerConnId) continue;
                            _bridge.ServerSyncAttributes(observers[i], networkId, publicData);
                        }
                    }
                }

                set.ClearDirty();
            }
        }

        /// <summary>
        /// Send full attribute sync to a specific client (join/reconnect).
        /// </summary>
        public void SendFullSync(INetConnection client, uint networkId,
            IReadOnlyList<(int attributeId, float baseValue, float currentValue)> allAttributes,
            bool isOwner)
        {
            EnsureArrayCapacity(ref _fullSyncEntries, allAttributes.Count);
            int count = 0;

            for (int i = 0; i < allAttributes.Count; i++)
            {
                var (attrId, baseVal, curVal) = allAttributes[i];

                // Non-owners only get public attributes
                if (!isOwner && !_publicAttributes.Contains(attrId))
                    continue;

                _fullSyncEntries[count++] = new AttributeEntry
                {
                    AttributeId = attrId,
                    BaseValue = baseVal,
                    CurrentValue = curVal
                };
            }

            var data = new AttributeUpdateData
            {
                TargetNetworkId = networkId,
                IsFullSync = true,
                AttributeCount = count,
                Attributes = _fullSyncEntries
            };

            _bridge.ServerSyncAttributes(client, networkId, data);
        }

        private static void EnsureArrayCapacity(ref AttributeEntry[] entries, int capacity)
        {
            if (entries == null || entries.Length < capacity)
            {
                int current = entries != null ? entries.Length : 0;
                int next = Math.Max(capacity, current == 0 ? 4 : current * 2);
                entries = new AttributeEntry[next];
            }
        }

        private sealed class DirtyAttributeSet
        {
            private AttributeEntry[] _dirtyEntries;
            private int _dirtyCount;
            private AttributeEntry[] _allDirtyEntries = Array.Empty<AttributeEntry>();
            private AttributeEntry[] _publicDirtyEntries = Array.Empty<AttributeEntry>();
            public int DirtyCount => _dirtyCount;

            public DirtyAttributeSet(int initialCapacity)
            {
                _dirtyEntries = new AttributeEntry[Math.Max(1, initialCapacity)];
            }

            public void SetDirty(int attributeId, float baseValue, float currentValue)
            {
                for (int i = 0; i < _dirtyCount; i++)
                {
                    if (_dirtyEntries[i].AttributeId != attributeId)
                        continue;

                    _dirtyEntries[i] = new AttributeEntry
                    {
                        AttributeId = attributeId,
                        BaseValue = baseValue,
                        CurrentValue = currentValue
                    };
                    return;
                }

                EnsureArrayCapacity(ref _dirtyEntries, _dirtyCount + 1);
                _dirtyEntries[_dirtyCount++] = new AttributeEntry
                {
                    AttributeId = attributeId,
                    BaseValue = baseValue,
                    CurrentValue = currentValue
                };
            }

            public AttributeUpdateData BuildUpdateData(uint networkId, bool includeAll,
                HashSet<int> publicFilter = null)
            {
                ref var entries = ref (includeAll ? ref _allDirtyEntries : ref _publicDirtyEntries);
                EnsureArrayCapacity(ref entries, _dirtyCount);
                int count = 0;

                for (int i = 0; i < _dirtyCount; i++)
                {
                    var entry = _dirtyEntries[i];
                    if (!includeAll && publicFilter != null && !publicFilter.Contains(entry.AttributeId))
                        continue;

                    entries[count++] = entry;
                }

                return new AttributeUpdateData
                {
                    TargetNetworkId = networkId,
                    IsFullSync = false,
                    AttributeCount = count,
                    Attributes = entries
                };
            }

            public void ClearDirty()
            {
                _dirtyCount = 0;
            }
        }
    }
}
