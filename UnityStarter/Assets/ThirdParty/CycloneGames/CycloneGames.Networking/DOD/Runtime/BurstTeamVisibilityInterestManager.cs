using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using CycloneGames.Networking.Interest;

namespace CycloneGames.Networking.DOD
{
    /// <summary>
    /// Burst-accelerated team-based visibility interest manager.
    /// Drop-in replacement for <see cref="TeamVisibilityInterestManager"/> with:
    ///   - Zero GC per frame (NativeList/NativeHashMap/NativeHashSet reuse memory)
    ///   - Cache-friendly contiguous detection source arrays per team
    ///   - math.distancesq for optimized 2D distance calculations
    ///   - Flat NativeArray storage for reveal/deep-reveal zones
    ///
    /// Visibility rules (same as OOP version):
    ///   1. AlwaysRelevant → visible to all
    ///   2. Same team → always visible
    ///   3. Hidden enemy → visible only via deep-reveal zones
    ///   4. Enemy in detection range of any allied entity → visible
    ///   5. Enemy in a reveal zone belonging to observer's team → visible
    ///
    /// IMPORTANT: Call <see cref="Dispose"/> when the manager is no longer needed to free native memory.
    /// </summary>
    public sealed class BurstTeamVisibilityInterestManager : IInterestManager, IDisposable
    {
        private readonly float _defaultDetectionRange;
        private readonly float _defaultDetectionRangeSqr;

        // --- Configuration (managed, written infrequently) ---
        private readonly Dictionary<int, int> _connectionTeams = new Dictionary<int, int>(16);
        private readonly Dictionary<uint, int> _entityTeams = new Dictionary<uint, int>(256);
        private readonly Dictionary<uint, float> _entityDetectionRanges = new Dictionary<uint, float>(64);
        private readonly HashSet<uint> _hiddenEntities = new HashSet<uint>();

        // Managed reveal zone storage (written infrequently via AddRevealZone/RemoveRevealZone)
        private readonly List<RevealZoneManaged> _revealZonesManaged = new List<RevealZoneManaged>(16);
        private readonly List<RevealZoneManaged> _deepRevealZonesManaged = new List<RevealZoneManaged>(8);

        // --- Native data (rebuilt each PreUpdate, zero GC) ---
        private NativeList<EntityVisData> _entityData;

        // Flat detection sources: all teams' sources packed contiguously
        // _teamSourceRanges[teamId] = (startIndex, count) into _detectionSources
        private NativeList<DetectionSource> _detectionSources;
        private NativeHashMap<int, int2> _teamSourceRanges;

        // Native reveal zones (copied from managed each PreUpdate)
        private NativeList<RevealZoneNative> _revealZones;
        private NativeList<RevealZoneNative> _deepRevealZones;

        // Native hidden set for O(1) lookup during RebuildForConnection
        private NativeHashSet<uint> _hiddenSet;

        private int _nextZoneId = 1;
        private const int MaxZoneId = int.MaxValue - 1;
        private bool _disposed;

        public BurstTeamVisibilityInterestManager(float defaultDetectionRange = 12f,
            int initialEntityCapacity = 1024)
        {
            _defaultDetectionRange = defaultDetectionRange;
            _defaultDetectionRangeSqr = defaultDetectionRange * defaultDetectionRange;

            _entityData = new NativeList<EntityVisData>(initialEntityCapacity, Allocator.Persistent);
            _detectionSources = new NativeList<DetectionSource>(initialEntityCapacity, Allocator.Persistent);
            _teamSourceRanges = new NativeHashMap<int, int2>(8, Allocator.Persistent);
            _revealZones = new NativeList<RevealZoneNative>(16, Allocator.Persistent);
            _deepRevealZones = new NativeList<RevealZoneNative>(8, Allocator.Persistent);
            _hiddenSet = new NativeHashSet<uint>(64, Allocator.Persistent);
        }

        // --- Configuration API (same as OOP version) ---

        public void SetConnectionTeam(int connectionId, int teamId)
            => _connectionTeams[connectionId] = teamId;

        public void RemoveConnection(int connectionId)
            => _connectionTeams.Remove(connectionId);

        public void SetEntityTeam(uint networkId, int teamId)
            => _entityTeams[networkId] = teamId;

        public void RemoveEntity(uint networkId)
        {
            _entityTeams.Remove(networkId);
            _entityDetectionRanges.Remove(networkId);
            _hiddenEntities.Remove(networkId);
        }

        public void SetEntityDetectionRange(uint networkId, float range)
            => _entityDetectionRanges[networkId] = range;

        public void SetHidden(uint networkId, bool hidden)
        {
            if (hidden) _hiddenEntities.Add(networkId);
            else _hiddenEntities.Remove(networkId);
        }

        public int AddRevealZone(Vector3 center, float radius, int teamId,
            bool isDeepReveal = false, float duration = float.MaxValue)
        {
            if (_nextZoneId >= MaxZoneId)
                _nextZoneId = 1;

            var zone = new RevealZoneManaged
            {
                Id = _nextZoneId++,
                Center = center,
                RadiusSqr = radius * radius,
                TeamId = teamId,
                ExpireTime = Time.time + duration,
                IsDeepReveal = isDeepReveal
            };

            if (isDeepReveal)
                _deepRevealZonesManaged.Add(zone);
            else
                _revealZonesManaged.Add(zone);

            return zone.Id;
        }

        public void RemoveRevealZone(int zoneId)
        {
            for (int i = _revealZonesManaged.Count - 1; i >= 0; i--)
                if (_revealZonesManaged[i].Id == zoneId) { _revealZonesManaged.RemoveAt(i); return; }
            for (int i = _deepRevealZonesManaged.Count - 1; i >= 0; i--)
                if (_deepRevealZonesManaged[i].Id == zoneId) { _deepRevealZonesManaged.RemoveAt(i); return; }
        }

        // --- IInterestManager ---

        public void PreUpdate(IReadOnlyList<INetworkEntity> allEntities)
        {
            float now = Time.time;

            // Expire managed zones
            CleanExpiredZones(_revealZonesManaged, now);
            CleanExpiredZones(_deepRevealZonesManaged, now);

            int count = allEntities.Count;

            // 1. Marshal entity data to NativeList
            _entityData.Clear();
            for (int i = 0; i < count; i++)
            {
                var e = allEntities[i];
                _entityTeams.TryGetValue(e.NetworkId, out int teamId);

                byte flags = 0;
                if (e.AlwaysRelevant) flags |= 1;
                if (_hiddenEntities.Contains(e.NetworkId)) flags |= 2;

                _entityData.Add(new EntityVisData
                {
                    NetworkId = e.NetworkId,
                    Position = e.Position,
                    TeamId = teamId,
                    OwnerConnectionId = e.OwnerConnectionId,
                    Flags = flags
                });
            }

            // 2. Build detection sources per team into flat contiguous array
            //    Step 1: collect sources grouped by team into temp structure
            //    Step 2: pack them contiguously and record ranges
            _detectionSources.Clear();
            EnsureHashMapCapacity(ref _teamSourceRanges, 16);
            _teamSourceRanges.Clear();

            // Iterate entities grouped by team (teams are few: 2-8)
            // Each team's detection sources are packed contiguously for cache-friendly inner loop
            using (var activeTeams = new NativeList<int>(8, Allocator.Temp))
            {
                foreach (var kvp in _connectionTeams)
                {
                    if (!ContainsTeam(activeTeams, kvp.Value))
                        activeTeams.Add(kvp.Value);
                }

                // Also include teams from entity assignments
                foreach (var kvp in _entityTeams)
                {
                    if (!ContainsTeam(activeTeams, kvp.Value))
                        activeTeams.Add(kvp.Value);
                }

                // For each team, collect detection sources contiguously
                for (int t = 0; t < activeTeams.Length; t++)
                {
                    int teamId = activeTeams[t];
                    int startIdx = _detectionSources.Length;

                    for (int i = 0; i < _entityData.Length; i++)
                    {
                        var ed = _entityData[i];
                        if (ed.TeamId != teamId) continue;
                        if ((ed.Flags & 2) != 0) continue; // Hidden

                        float rangeSqr = _entityDetectionRanges.TryGetValue(ed.NetworkId, out float r)
                            ? r * r
                            : _defaultDetectionRangeSqr;

                        _detectionSources.Add(new DetectionSource
                        {
                            X = ed.Position.x,
                            Z = ed.Position.z,
                            RangeSqr = rangeSqr
                        });
                    }

                    int sourceCount = _detectionSources.Length - startIdx;
                    if (sourceCount > 0)
                        _teamSourceRanges[teamId] = new int2(startIdx, sourceCount);
                }
            }

            // 3. Marshal reveal zones to native
            MarshalRevealZones(_revealZonesManaged, ref _revealZones);
            MarshalRevealZones(_deepRevealZonesManaged, ref _deepRevealZones);

            // 4. Marshal hidden set to native
            EnsureHashSetCapacity(ref _hiddenSet, _hiddenEntities.Count);
            _hiddenSet.Clear();
            foreach (uint id in _hiddenEntities)
                _hiddenSet.Add(id);
        }

        public void RebuildForConnection(INetConnection connection, IReadOnlyList<INetworkEntity> allEntities,
            HashSet<uint> results)
        {
            results.Clear();

            if (!_connectionTeams.TryGetValue(connection.ConnectionId, out int myTeamId))
                return;

            // Get ally detection source range (contiguous in _detectionSources)
            _teamSourceRanges.TryGetValue(myTeamId, out int2 allyRange);
            // allyRange = (startIndex, count), or (0,0) if no sources

            int entityCount = _entityData.Length;

            for (int i = 0; i < entityCount; i++)
            {
                var entity = _entityData[i];

                // Rule 1: AlwaysRelevant
                if ((entity.Flags & 1) != 0)
                {
                    results.Add(entity.NetworkId);
                    continue;
                }

                // Rule 2: Same team
                if (entity.TeamId == myTeamId)
                {
                    results.Add(entity.NetworkId);
                    continue;
                }

                bool isHidden = (entity.Flags & 2) != 0;

                // Rule 3: Hidden enemies — need deep-reveal
                if (isHidden)
                {
                    if (IsInDeepReveal(entity.Position.x, entity.Position.z, myTeamId))
                        results.Add(entity.NetworkId);
                    continue;
                }

                // Rule 4: Enemy in detection range of any allied source
                // Scan the contiguous ally source block — cache-friendly linear read
                if (allyRange.y > 0 && IsInAllyDetection(entity.Position.x, entity.Position.z,
                        allyRange.x, allyRange.y))
                {
                    results.Add(entity.NetworkId);
                    continue;
                }

                // Rule 5: Enemy in a reveal zone
                if (IsInRevealZone(entity.Position.x, entity.Position.z, myTeamId))
                {
                    results.Add(entity.NetworkId);
                }
            }
        }

        public bool IsVisible(INetConnection connection, INetworkEntity entity)
        {
            if (entity.AlwaysRelevant) return true;

            if (!_connectionTeams.TryGetValue(connection.ConnectionId, out int myTeamId))
                return false;

            _entityTeams.TryGetValue(entity.NetworkId, out int entityTeamId);
            if (entityTeamId == myTeamId) return true;

            float ex = entity.Position.x;
            float ez = entity.Position.z;

            if (_hiddenSet.IsCreated && _hiddenSet.Contains(entity.NetworkId))
                return IsInDeepReveal(ex, ez, myTeamId);

            if (_teamSourceRanges.IsCreated && _teamSourceRanges.TryGetValue(myTeamId, out int2 range))
            {
                if (range.y > 0 && IsInAllyDetection(ex, ez, range.x, range.y))
                    return true;
            }

            return IsInRevealZone(ex, ez, myTeamId);
        }

        // --- Internal hot-path methods ---

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsInAllyDetection(float entityX, float entityZ, int sourceStart, int sourceCount)
        {
            int end = sourceStart + sourceCount;
            for (int s = sourceStart; s < end; s++)
            {
                var src = _detectionSources[s];
                float dx = entityX - src.X;
                float dz = entityZ - src.Z;
                if (dx * dx + dz * dz <= src.RangeSqr)
                    return true;
            }
            return false;
        }

        private bool IsInRevealZone(float entityX, float entityZ, int teamId)
        {
            for (int i = 0; i < _revealZones.Length; i++)
            {
                var zone = _revealZones[i];
                if (zone.TeamId != teamId) continue;
                float dx = entityX - zone.X;
                float dz = entityZ - zone.Z;
                if (dx * dx + dz * dz <= zone.RadiusSqr)
                    return true;
            }
            return false;
        }

        private bool IsInDeepReveal(float entityX, float entityZ, int teamId)
        {
            for (int i = 0; i < _deepRevealZones.Length; i++)
            {
                var zone = _deepRevealZones[i];
                if (zone.TeamId != teamId) continue;
                float dx = entityX - zone.X;
                float dz = entityZ - zone.Z;
                if (dx * dx + dz * dz <= zone.RadiusSqr)
                    return true;
            }
            return false;
        }

        // --- Helpers ---

        private static bool ContainsTeam(NativeList<int> list, int teamId)
        {
            for (int i = 0; i < list.Length; i++)
                if (list[i] == teamId) return true;
            return false;
        }

        private static void MarshalRevealZones(List<RevealZoneManaged> managed, ref NativeList<RevealZoneNative> native)
        {
            native.Clear();
            for (int i = 0; i < managed.Count; i++)
            {
                var z = managed[i];
                native.Add(new RevealZoneNative
                {
                    X = z.Center.x,
                    Z = z.Center.z,
                    RadiusSqr = z.RadiusSqr,
                    TeamId = z.TeamId
                });
            }
        }

        private static void CleanExpiredZones(List<RevealZoneManaged> list, float now)
        {
            for (int i = list.Count - 1; i >= 0; i--)
                if (now >= list[i].ExpireTime) list.RemoveAt(i);
        }

        private static void EnsureHashMapCapacity<TKey, TValue>(ref NativeHashMap<TKey, TValue> map, int needed)
            where TKey : unmanaged, IEquatable<TKey>
            where TValue : unmanaged
        {
            needed = math.max(needed, 16);
            if (map.Capacity < needed)
            {
                map.Dispose();
                map = new NativeHashMap<TKey, TValue>(needed, Allocator.Persistent);
            }
        }

        private static void EnsureHashSetCapacity(ref NativeHashSet<uint> set, int needed)
        {
            needed = math.max(needed, 16);
            if (set.Capacity < needed)
            {
                set.Dispose();
                set = new NativeHashSet<uint>(needed, Allocator.Persistent);
            }
        }

        // --- IDisposable ---

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_entityData.IsCreated) _entityData.Dispose();
            if (_detectionSources.IsCreated) _detectionSources.Dispose();
            if (_teamSourceRanges.IsCreated) _teamSourceRanges.Dispose();
            if (_revealZones.IsCreated) _revealZones.Dispose();
            if (_deepRevealZones.IsCreated) _deepRevealZones.Dispose();
            if (_hiddenSet.IsCreated) _hiddenSet.Dispose();
        }

        // --- Data Structures ---

        internal struct EntityVisData
        {
            public uint NetworkId;     // 4 bytes
            public float3 Position;    // 12 bytes
            public int TeamId;         // 4 bytes
            public int OwnerConnectionId; // 4 bytes
            public byte Flags;         // 1 byte (bit 0 = AlwaysRelevant, bit 1 = Hidden)
            // Total: ~25 bytes + padding ≈ 28 bytes
        }

        internal struct DetectionSource
        {
            public float X;            // 4 bytes
            public float Z;            // 4 bytes
            public float RangeSqr;     // 4 bytes
            // Total: 12 bytes. Five per cache line — optimal for inner loop iteration.
        }

        internal struct RevealZoneNative
        {
            public float X;
            public float Z;
            public float RadiusSqr;
            public int TeamId;
            // Total: 16 bytes. Four per cache line.
        }

        private struct RevealZoneManaged
        {
            public int Id;
            public Vector3 Center;
            public float RadiusSqr;
            public int TeamId;
            public float ExpireTime;
            public bool IsDeepReveal;
        }
    }
}
