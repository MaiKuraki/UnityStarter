using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace CycloneGames.Networking.Interest
{
    /// <summary>
    /// Spatial-grid based interest manager. O(1) per-entity hash, O(n) per-connection rebuild
    /// where n is the number of entities in nearby cells.
    /// Ideal for: open-world games (GTA, ARK), sandbox (Minecraft, building games),
    /// MMO (WoW, FFXIV), battle royale (PUBG).
    /// </summary>
    public sealed class GridInterestManager : IInterestManager
    {
        private readonly float _cellSize;
        private readonly float _visibilityRange;
        private readonly int _cellRange;        // cellRange = ceil(visibilityRange / cellSize)
        private readonly Dictionary<long, List<INetworkEntity>> _grid;

        // Reusable connection observer data: connectionId -> position
        private readonly Dictionary<int, Vector3> _observerPositions = new Dictionary<int, Vector3>();
        // connectionId -> relevance groups
        private readonly Dictionary<int, HashSet<int>> _observerGroups = new Dictionary<int, HashSet<int>>();

        public float CellSize => _cellSize;
        public float VisibilityRange => _visibilityRange;

        /// <param name="cellSize">World-space size of each grid cell. Larger = fewer cells, coarser culling.</param>
        /// <param name="visibilityRange">Max distance an entity is visible from an observer.</param>
        public GridInterestManager(float cellSize = 50f, float visibilityRange = 100f)
        {
            _cellSize = cellSize;
            _visibilityRange = visibilityRange;
            _cellRange = Mathf.CeilToInt(visibilityRange / cellSize);
            _grid = new Dictionary<long, List<INetworkEntity>>(256);
        }

        /// <summary>
        /// Register/update an observer's position. Call when a player moves or spawns.
        /// </summary>
        public void SetObserverPosition(int connectionId, Vector3 position)
        {
            _observerPositions[connectionId] = position;
        }

        public void RemoveObserver(int connectionId)
        {
            _observerPositions.Remove(connectionId);
            _observerGroups.Remove(connectionId);
        }

        public void SetObserverGroups(int connectionId, HashSet<int> groups)
        {
            _observerGroups[connectionId] = groups;
        }

        public void PreUpdate(IReadOnlyList<INetworkEntity> allEntities)
        {
            // Clear and rehash all entities into grid
            // Prune empty cells to prevent unbounded dictionary growth
            List<long> emptyKeys = null;
            foreach (var pair in _grid)
            {
                pair.Value.Clear();
            }

            for (int i = 0; i < allEntities.Count; i++)
            {
                var entity = allEntities[i];
                long key = CellKey(entity.Position);

                if (!_grid.TryGetValue(key, out var list))
                {
                    list = new List<INetworkEntity>(16);
                    _grid[key] = list;
                }
                list.Add(entity);
            }

            // Remove cells that stayed empty (entities moved away permanently)
            foreach (var pair in _grid)
            {
                if (pair.Value.Count == 0)
                {
                    emptyKeys ??= new List<long>(16);
                    emptyKeys.Add(pair.Key);
                }
            }
            if (emptyKeys != null)
            {
                for (int i = 0; i < emptyKeys.Count; i++)
                    _grid.Remove(emptyKeys[i]);
            }
        }

        public void RebuildForConnection(INetConnection connection, IReadOnlyList<INetworkEntity> allEntities, HashSet<uint> results)
        {
            results.Clear();

            if (!_observerPositions.TryGetValue(connection.ConnectionId, out var observerPos))
                return;

            _observerGroups.TryGetValue(connection.ConnectionId, out var groups);
            float rangeSq = _visibilityRange * _visibilityRange;

            int cx = CellCoord(observerPos.x);
            int cz = CellCoord(observerPos.z);

            // Scan nearby cells
            for (int dx = -_cellRange; dx <= _cellRange; dx++)
            {
                for (int dz = -_cellRange; dz <= _cellRange; dz++)
                {
                    long key = PackKey(cx + dx, cz + dz);
                    if (!_grid.TryGetValue(key, out var cell))
                        continue;

                    for (int i = 0; i < cell.Count; i++)
                    {
                        var entity = cell[i];

                        if (entity.AlwaysRelevant ||
                            entity.OwnerConnectionId == connection.ConnectionId)
                        {
                            results.Add(entity.NetworkId);
                            continue;
                        }

                        if (groups != null && entity.RelevanceGroup != 0 && groups.Contains(entity.RelevanceGroup))
                        {
                            results.Add(entity.NetworkId);
                            continue;
                        }

                        float distSq = (entity.Position - observerPos).sqrMagnitude;
                        if (distSq <= rangeSq)
                            results.Add(entity.NetworkId);
                    }
                }
            }

            // Always include AlwaysRelevant entities outside the grid scan
            for (int i = 0; i < allEntities.Count; i++)
            {
                if (allEntities[i].AlwaysRelevant)
                    results.Add(allEntities[i].NetworkId);
            }
        }

        public bool IsVisible(INetConnection connection, INetworkEntity entity)
        {
            if (entity.AlwaysRelevant || entity.OwnerConnectionId == connection.ConnectionId)
                return true;

            if (!_observerPositions.TryGetValue(connection.ConnectionId, out var observerPos))
                return false;

            if (_observerGroups.TryGetValue(connection.ConnectionId, out var groups) &&
                entity.RelevanceGroup != 0 && groups.Contains(entity.RelevanceGroup))
                return true;

            return (entity.Position - observerPos).sqrMagnitude <= _visibilityRange * _visibilityRange;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int CellCoord(float worldCoord) => Mathf.FloorToInt(worldCoord / _cellSize);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long CellKey(Vector3 pos) => PackKey(CellCoord(pos.x), CellCoord(pos.z));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long PackKey(int x, int z) => ((long)x << 32) | (uint)z;
    }
}
