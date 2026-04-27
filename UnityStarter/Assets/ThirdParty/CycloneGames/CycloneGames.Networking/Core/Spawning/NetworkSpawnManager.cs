using System;
using System.Collections.Generic;

namespace CycloneGames.Networking.Spawning
{
    public interface INetworkSpawnManager
    {
        IReadOnlyDictionary<uint, ISpawnedObject> SpawnedObjects { get; }

        uint Spawn(ISpawnedObject obj, INetConnection owner = null);
        void Despawn(uint networkId);
        void TransferOwnership(uint networkId, INetConnection newOwner);

        event Action<ISpawnedObject> OnSpawned;
        event Action<ISpawnedObject> OnDespawned;
        event Action<ISpawnedObject, INetConnection, INetConnection> OnOwnershipChanged;
    }

    public interface ISpawnedObject
    {
        uint NetworkId { get; set; }
        int PrefabId { get; }
        int OwnerConnectionId { get; set; }
        bool HasAuthority { get; set; }

        void OnNetworkSpawn();
        void OnNetworkDespawn();
        void OnGainedAuthority();
        void OnLostAuthority();
    }

    public sealed class NetworkSpawnManager : INetworkSpawnManager
    {
        private readonly Dictionary<uint, ISpawnedObject> _spawned = new Dictionary<uint, ISpawnedObject>(256);
        private readonly Dictionary<int, INetConnection> _connectionCache = new Dictionary<int, INetConnection>(16);
        private uint _nextNetworkId = 1;

        public IReadOnlyDictionary<uint, ISpawnedObject> SpawnedObjects => _spawned;

        public event Action<ISpawnedObject> OnSpawned;
        public event Action<ISpawnedObject> OnDespawned;
        public event Action<ISpawnedObject, INetConnection, INetConnection> OnOwnershipChanged;

        public uint Spawn(ISpawnedObject obj, INetConnection owner = null)
        {
            if (_nextNetworkId == 0)
                throw new OverflowException("NetworkSpawnManager: Network ID space exhausted (uint.MaxValue reached).");

            uint id = _nextNetworkId++;
            obj.NetworkId = id;
            obj.OwnerConnectionId = owner?.ConnectionId ?? -1;
            obj.HasAuthority = owner != null;

            _spawned[id] = obj;
            if (owner != null)
                _connectionCache[owner.ConnectionId] = owner;

            obj.OnNetworkSpawn();
            OnSpawned?.Invoke(obj);
            return id;
        }

        public void Despawn(uint networkId)
        {
            if (!_spawned.TryGetValue(networkId, out var obj)) return;

            obj.OnNetworkDespawn();
            _spawned.Remove(networkId);
            OnDespawned?.Invoke(obj);
        }

        public void TransferOwnership(uint networkId, INetConnection newOwner)
        {
            if (!_spawned.TryGetValue(networkId, out var obj)) return;

            int oldOwnerId = obj.OwnerConnectionId;
            _connectionCache.TryGetValue(oldOwnerId, out var oldOwner);

            obj.OnLostAuthority();
            obj.OwnerConnectionId = newOwner?.ConnectionId ?? -1;
            obj.HasAuthority = newOwner != null;
            if (newOwner != null)
                _connectionCache[newOwner.ConnectionId] = newOwner;
            obj.OnGainedAuthority();

            OnOwnershipChanged?.Invoke(obj, oldOwner, newOwner);
        }

        public bool TryGet(uint networkId, out ISpawnedObject obj) => _spawned.TryGetValue(networkId, out obj);

        public void DespawnAll()
        {
            foreach (var kvp in _spawned)
                kvp.Value.OnNetworkDespawn();
            _spawned.Clear();
        }

        public void Reset()
        {
            DespawnAll();
            _nextNetworkId = 1;
            _connectionCache.Clear();
        }
    }
}
