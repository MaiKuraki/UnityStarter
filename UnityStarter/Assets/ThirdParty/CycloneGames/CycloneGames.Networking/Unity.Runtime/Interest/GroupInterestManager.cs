using System.Collections.Generic;

namespace CycloneGames.Networking.Interest
{
    /// <summary>
    /// Manual group-based interest manager for instanced/partitioned worlds.
    /// Use for: dungeon instances (FFXIV, WoW), lobby rooms (Mario Kart),
    /// private sessions (Monster Hunter), building plots (sandbox games).
    /// 
    /// Entities and connections are assigned to groups. An observer sees
    /// all entities in its groups plus AlwaysRelevant entities.
    /// </summary>
    public sealed class GroupInterestManager : IInterestManager
    {
        // groupId -> set of entity network IDs
        private readonly Dictionary<int, HashSet<uint>> _groupEntities = new Dictionary<int, HashSet<uint>>();
        // connectionId -> set of group IDs the observer belongs to
        private readonly Dictionary<int, HashSet<int>> _connectionGroups = new Dictionary<int, HashSet<int>>();
        // cache: entity networkId -> its groupId
        private readonly Dictionary<uint, int> _entityGroupMap = new Dictionary<uint, int>();

        public void AddEntityToGroup(int groupId, uint entityNetworkId)
        {
            if (!_groupEntities.TryGetValue(groupId, out var set))
            {
                set = new HashSet<uint>();
                _groupEntities[groupId] = set;
            }
            set.Add(entityNetworkId);
            _entityGroupMap[entityNetworkId] = groupId;
        }

        public void RemoveEntityFromGroup(int groupId, uint entityNetworkId)
        {
            if (_groupEntities.TryGetValue(groupId, out var set))
                set.Remove(entityNetworkId);
            _entityGroupMap.Remove(entityNetworkId);
        }

        public void AddConnectionToGroup(int connectionId, int groupId)
        {
            if (!_connectionGroups.TryGetValue(connectionId, out var groups))
            {
                groups = new HashSet<int>();
                _connectionGroups[connectionId] = groups;
            }
            groups.Add(groupId);
        }

        public void RemoveConnectionFromGroup(int connectionId, int groupId)
        {
            if (_connectionGroups.TryGetValue(connectionId, out var groups))
                groups.Remove(groupId);
        }

        public void RemoveConnection(int connectionId)
        {
            _connectionGroups.Remove(connectionId);
        }

        public void RemoveGroup(int groupId)
        {
            if (_groupEntities.TryGetValue(groupId, out var entities))
            {
                foreach (uint id in entities)
                    _entityGroupMap.Remove(id);
                _groupEntities.Remove(groupId);
            }

            // Clean up stale references in all connection group sets
            foreach (var pair in _connectionGroups)
            {
                pair.Value.Remove(groupId);
            }
        }

        public void PreUpdate(IReadOnlyList<INetworkEntity> allEntities)
        {
            // Group assignment is manual; no per-tick spatial rebuild needed
        }

        public void RebuildForConnection(INetConnection connection, IReadOnlyList<INetworkEntity> allEntities, HashSet<uint> results)
        {
            results.Clear();

            _connectionGroups.TryGetValue(connection.ConnectionId, out var myGroups);

            for (int i = 0; i < allEntities.Count; i++)
            {
                var entity = allEntities[i];

                if (entity.AlwaysRelevant || entity.OwnerConnectionId == connection.ConnectionId)
                {
                    results.Add(entity.NetworkId);
                    continue;
                }

                if (myGroups != null)
                {
                    // Check if entity's group matches any of connection's groups
                    if (entity.RelevanceGroup != 0 && myGroups.Contains(entity.RelevanceGroup))
                    {
                        results.Add(entity.NetworkId);
                        continue;
                    }

                    // Check via entity group map
                    if (_entityGroupMap.TryGetValue(entity.NetworkId, out int entityGroup) && myGroups.Contains(entityGroup))
                    {
                        results.Add(entity.NetworkId);
                    }
                }
            }
        }

        public bool IsVisible(INetConnection connection, INetworkEntity entity)
        {
            if (entity.AlwaysRelevant || entity.OwnerConnectionId == connection.ConnectionId)
                return true;

            if (!_connectionGroups.TryGetValue(connection.ConnectionId, out var myGroups))
                return false;

            if (entity.RelevanceGroup != 0 && myGroups.Contains(entity.RelevanceGroup))
                return true;

            return _entityGroupMap.TryGetValue(entity.NetworkId, out int g) && myGroups.Contains(g);
        }
    }
}
