using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Mathematics;
using CycloneGames.Networking.Interest;

namespace CycloneGames.Networking.DOD
{
    /// <summary>
    /// Burst-accelerated spatial-grid interest manager using sort-based spatial hashing.
    /// Drop-in replacement for <see cref="GridInterestManager"/> with:
    ///   - Zero GC per frame (NativeList/NativeHashMap reuse memory)
    ///   - Cache-friendly contiguous memory layout (sorted entities)
    ///   - Burst-compiled NativeArray.Sort() for O(n log n) spatial indexing
    ///   - math.distancesq for optimized distance calculations
    ///
    /// Recommended for 5,000+ entities. For smaller entity counts, the standard
    /// <see cref="GridInterestManager"/> is sufficient and simpler to use.
    ///
    /// Architecture:
    ///   PreUpdate:  Marshal entities → compute cell keys → Burst sort → build cell range index
    ///   Rebuild:    Lookup nearby cells in sorted array → linear scan with distance check
    ///
    /// IMPORTANT: Call <see cref="Dispose"/> when the manager is no longer needed to free native memory.
    /// </summary>
    public sealed class BurstGridInterestManager : IInterestManager, IDisposable
    {
        private readonly float _cellSize;
        private readonly float _invCellSize;
        private readonly float _visibilityRange;
        private readonly float _visibilityRangeSqr;
        private readonly int _cellRange;

        // Native data — reused across frames, zero GC
        private NativeList<SortedEntity> _entities;
        private NativeHashMap<long, int2> _cellRanges;   // cellKey → (startIndex, count) in sorted array
        private NativeList<uint> _alwaysRelevant;

        // Observer state
        private NativeHashMap<int, float3> _observerPositions;
        private readonly Dictionary<int, HashSet<int>> _observerGroups = new Dictionary<int, HashSet<int>>();

        private bool _disposed;

        public float CellSize => _cellSize;
        public float VisibilityRange => _visibilityRange;

        /// <param name="cellSize">World-space size of each grid cell. Larger = fewer cells, coarser culling.</param>
        /// <param name="visibilityRange">Max distance an entity is visible from an observer.</param>
        /// <param name="initialCapacity">Pre-allocated entity capacity. Grows automatically if exceeded.</param>
        public BurstGridInterestManager(float cellSize = 50f, float visibilityRange = 100f, int initialCapacity = 1024)
        {
            _cellSize = cellSize;
            _invCellSize = 1f / cellSize;
            _visibilityRange = visibilityRange;
            _visibilityRangeSqr = visibilityRange * visibilityRange;
            _cellRange = (int)math.ceil(visibilityRange / cellSize);

            _entities = new NativeList<SortedEntity>(initialCapacity, Allocator.Persistent);
            _cellRanges = new NativeHashMap<long, int2>(initialCapacity / 4, Allocator.Persistent);
            _alwaysRelevant = new NativeList<uint>(64, Allocator.Persistent);
            _observerPositions = new NativeHashMap<int, float3>(16, Allocator.Persistent);
        }

        // --- Configuration API ---

        public void SetObserverPosition(int connectionId, float3 position)
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

        // --- IInterestManager ---

        public void PreUpdate(IReadOnlyList<INetworkEntity> allEntities)
        {
            int count = allEntities.Count;

            // 1. Marshal managed entities → NativeList with inline cell key computation
            _entities.Clear();
            _alwaysRelevant.Clear();

            for (int i = 0; i < count; i++)
            {
                var e = allEntities[i];
                float3 pos = e.Position;
                int cx = (int)math.floor(pos.x * _invCellSize);
                int cz = (int)math.floor(pos.z * _invCellSize);

                byte flags = 0;
                if (e.AlwaysRelevant)
                {
                    flags = 1;
                    _alwaysRelevant.Add(e.NetworkId);
                }

                _entities.Add(new SortedEntity
                {
                    CellKey = PackKey(cx, cz),
                    NetworkId = e.NetworkId,
                    Position = pos,
                    OwnerConnectionId = e.OwnerConnectionId,
                    RelevanceGroup = e.RelevanceGroup,
                    Flags = flags
                });
            }

            // 2. Sort by cell key — Burst-compiled IntroSort via NativeSortExtension
            if (count > 1)
                _entities.AsArray().Sort();

            // 3. Build cell range index: linear scan over sorted data → HashMap<cellKey, (start, count)>
            EnsureHashMapCapacity(ref _cellRanges, count);
            _cellRanges.Clear();

            if (count > 0)
            {
                long currentKey = _entities[0].CellKey;
                int startIdx = 0;

                for (int i = 1; i < count; i++)
                {
                    long key = _entities[i].CellKey;
                    if (key != currentKey)
                    {
                        _cellRanges.Add(currentKey, new int2(startIdx, i - startIdx));
                        currentKey = key;
                        startIdx = i;
                    }
                }
                _cellRanges.Add(currentKey, new int2(startIdx, count - startIdx));
            }
        }

        public void RebuildForConnection(INetConnection connection, IReadOnlyList<INetworkEntity> allEntities,
            HashSet<uint> results)
        {
            results.Clear();

            // Always-relevant entities (captured during PreUpdate, no need to re-scan allEntities)
            for (int i = 0; i < _alwaysRelevant.Length; i++)
                results.Add(_alwaysRelevant[i]);

            int connId = connection.ConnectionId;
            if (!_observerPositions.TryGetValue(connId, out float3 observerPos))
                return;

            _observerGroups.TryGetValue(connId, out var groups);

            int cx = (int)math.floor(observerPos.x * _invCellSize);
            int cz = (int)math.floor(observerPos.z * _invCellSize);

            // Scan sorted entities in nearby cells — contiguous memory access
            for (int dx = -_cellRange; dx <= _cellRange; dx++)
            {
                for (int dz = -_cellRange; dz <= _cellRange; dz++)
                {
                    long key = PackKey(cx + dx, cz + dz);
                    if (!_cellRanges.TryGetValue(key, out int2 range))
                        continue;

                    int end = range.x + range.y;
                    for (int i = range.x; i < end; i++)
                    {
                        var entity = _entities[i];

                        // Owner or always-relevant: skip distance check
                        if ((entity.Flags & 1) != 0 || entity.OwnerConnectionId == connId)
                        {
                            results.Add(entity.NetworkId);
                            continue;
                        }

                        // Relevance group match
                        if (groups != null && entity.RelevanceGroup != 0
                            && groups.Contains(entity.RelevanceGroup))
                        {
                            results.Add(entity.NetworkId);
                            continue;
                        }

                        // Distance check using math.distancesq (optimized for contiguous float3 data)
                        if (math.distancesq(entity.Position, observerPos) <= _visibilityRangeSqr)
                            results.Add(entity.NetworkId);
                    }
                }
            }
        }

        public bool IsVisible(INetConnection connection, INetworkEntity entity)
        {
            if (entity.AlwaysRelevant || entity.OwnerConnectionId == connection.ConnectionId)
                return true;

            if (!_observerPositions.TryGetValue(connection.ConnectionId, out float3 observerPos))
                return false;

            if (_observerGroups.TryGetValue(connection.ConnectionId, out var groups)
                && entity.RelevanceGroup != 0 && groups.Contains(entity.RelevanceGroup))
                return true;

            return math.distancesq((float3)entity.Position, observerPos) <= _visibilityRangeSqr;
        }

        // --- Internals ---

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long PackKey(int x, int z) => ((long)x << 32) | (uint)z;

        private static void EnsureHashMapCapacity<TKey, TValue>(ref NativeHashMap<TKey, TValue> map, int needed)
            where TKey : unmanaged, IEquatable<TKey>
            where TValue : unmanaged
        {
            needed = math.max(needed, 64);
            if (map.Capacity < needed)
            {
                map.Dispose();
                map = new NativeHashMap<TKey, TValue>(needed, Allocator.Persistent);
            }
        }

        // --- IDisposable ---

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_entities.IsCreated) _entities.Dispose();
            if (_cellRanges.IsCreated) _cellRanges.Dispose();
            if (_alwaysRelevant.IsCreated) _alwaysRelevant.Dispose();
            if (_observerPositions.IsCreated) _observerPositions.Dispose();
        }

        // --- Data Structures ---

        internal struct SortedEntity : IComparable<SortedEntity>
        {
            public long CellKey;       // 8 bytes — packed (cellX << 32 | cellZ)
            public uint NetworkId;     // 4 bytes
            public float3 Position;    // 12 bytes
            public int OwnerConnectionId; // 4 bytes
            public int RelevanceGroup; // 4 bytes
            public byte Flags;         // 1 byte (bit 0 = AlwaysRelevant)
            // Total: ~33 bytes + padding ≈ 36 bytes. Two per cache line.

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public int CompareTo(SortedEntity other) => CellKey.CompareTo(other.CellKey);
        }
    }
}
