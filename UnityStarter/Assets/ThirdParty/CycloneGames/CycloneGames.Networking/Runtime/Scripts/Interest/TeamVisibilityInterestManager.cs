using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace CycloneGames.Networking.Interest
{
    /// <summary>
    /// Team-based visibility interest manager.
    /// 
    /// An entity is visible to a connection if ANY of these conditions are met:
    /// 1. The entity is AlwaysRelevant (global objects, objectives)
    /// 2. The entity belongs to the same team as the observer
    /// 3. ANY allied entity within detection range has line-of-sight to the target
    /// 4. The entity is within a revealed area (e.g. sensor, scout ability)
    /// 
    /// Hidden entities (stealth/invisibility) require deep-reveal zones to become visible.
    /// Suitable for any team-based game with fog-of-war mechanics (MOBA, RTS, tactical, etc.)
    /// </summary>
    public sealed class TeamVisibilityInterestManager : IInterestManager
    {
        private readonly float _defaultDetectionRange;
        private readonly float _defaultDetectionRangeSqr;

        // Team assignments: connectionId -> teamId
        private readonly Dictionary<int, int> _connectionTeams = new Dictionary<int, int>(16);

        // Entity detection ranges (override default): networkId -> range
        private readonly Dictionary<uint, float> _entityDetectionRanges = new Dictionary<uint, float>(64);

        // Entity team assignments: networkId -> teamId
        private readonly Dictionary<uint, int> _entityTeams = new Dictionary<uint, int>(256);

        // Detection providers per team: entities that grant vision
        // Rebuilt each PreUpdate for cache coherence
        private readonly Dictionary<int, List<DetectionSource>> _teamDetectionSources =
            new Dictionary<int, List<DetectionSource>>(4);

        // Temporary revealed areas (sensor, scout abilities, etc.)
        private readonly List<RevealZone> _revealZones = new List<RevealZone>(16);

        // Deep-reveal zones that also reveal hidden/cloaked entities
        private readonly List<RevealZone> _deepRevealZones = new List<RevealZone>(8);

        // Entities flagged as hidden — require deep-reveal to become visible
        private readonly HashSet<uint> _hiddenEntities = new HashSet<uint>();

        private static readonly List<DetectionSource> EmptyDetectionList = new List<DetectionSource>(0);

        public TeamVisibilityInterestManager(float defaultDetectionRange = 12f)
        {
            _defaultDetectionRange = defaultDetectionRange;
            _defaultDetectionRangeSqr = defaultDetectionRange * defaultDetectionRange;
        }

        // --- Configuration API ---

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

        /// <summary>
        /// Override detection range for a specific entity.
        /// </summary>
        public void SetEntityDetectionRange(uint networkId, float range)
            => _entityDetectionRanges[networkId] = range;

        /// <summary>
        /// Mark an entity as hidden/cloaked. It will only be visible via deep-reveal zones.
        /// </summary>
        public void SetHidden(uint networkId, bool hidden)
        {
            if (hidden) _hiddenEntities.Add(networkId);
            else _hiddenEntities.Remove(networkId);
        }

        /// <summary>
        /// Add a temporary reveal zone (sensor placement, scout ability, etc.)
        /// Set <paramref name="isDeepReveal"/> to true to also reveal hidden entities.
        /// Returns an ID to remove it later.
        /// </summary>
        public int AddRevealZone(Vector3 center, float radius, int teamId,
            bool isDeepReveal = false, float duration = float.MaxValue)
        {
            if (_nextZoneId >= MaxZoneId)
                _nextZoneId = 1; // Wrap around (old zones should be cleaned by now)

            var zone = new RevealZone
            {
                Id = _nextZoneId++,
                Center = center,
                RadiusSqr = radius * radius,
                TeamId = teamId,
                ExpireTime = Time.time + duration,
                IsDeepReveal = isDeepReveal
            };

            if (isDeepReveal)
                _deepRevealZones.Add(zone);
            else
                _revealZones.Add(zone);

            return zone.Id;
        }

        public void RemoveRevealZone(int zoneId)
        {
            for (int i = _revealZones.Count - 1; i >= 0; i--)
                if (_revealZones[i].Id == zoneId) { _revealZones.RemoveAt(i); return; }
            for (int i = _deepRevealZones.Count - 1; i >= 0; i--)
                if (_deepRevealZones[i].Id == zoneId) { _deepRevealZones.RemoveAt(i); return; }
        }

        // --- IInterestManager ---

        public void PreUpdate(IReadOnlyList<INetworkEntity> allEntities)
        {
            // Clear and rebuild detection sources per team
            foreach (var pair in _teamDetectionSources)
                pair.Value.Clear();

            float now = Time.time;

            for (int i = 0; i < allEntities.Count; i++)
            {
                var entity = allEntities[i];
                if (!_entityTeams.TryGetValue(entity.NetworkId, out int teamId)) continue;

                // Hidden entities do not provide detection for their team
                if (_hiddenEntities.Contains(entity.NetworkId)) continue;

                float rangeSqr = _entityDetectionRanges.TryGetValue(entity.NetworkId, out float r)
                    ? r * r
                    : _defaultDetectionRangeSqr;

                if (!_teamDetectionSources.TryGetValue(teamId, out var sources))
                {
                    sources = new List<DetectionSource>(64);
                    _teamDetectionSources[teamId] = sources;
                }

                sources.Add(new DetectionSource { Position = entity.Position, RangeSqr = rangeSqr });
            }

            // Expire old reveal zones
            CleanExpiredZones(_revealZones, now);
            CleanExpiredZones(_deepRevealZones, now);
        }

        public void RebuildForConnection(INetConnection connection, IReadOnlyList<INetworkEntity> allEntities,
            HashSet<uint> results)
        {
            results.Clear();

            if (!_connectionTeams.TryGetValue(connection.ConnectionId, out int myTeamId))
                return;

            var allySources = _teamDetectionSources.TryGetValue(myTeamId, out var s) ? s : EmptyDetectionList;

            for (int i = 0; i < allEntities.Count; i++)
            {
                var entity = allEntities[i];

                // Rule 1: AlwaysRelevant
                if (entity.AlwaysRelevant)
                {
                    results.Add(entity.NetworkId);
                    continue;
                }

                _entityTeams.TryGetValue(entity.NetworkId, out int entityTeamId);

                // Rule 2: Same team — always visible
                if (entityTeamId == myTeamId)
                {
                    results.Add(entity.NetworkId);
                    continue;
                }

                bool isHidden = _hiddenEntities.Contains(entity.NetworkId);

                // Rule 3: Hidden enemies need deep-reveal
                if (isHidden)
                {
                    if (IsInDeepReveal(entity.Position, myTeamId))
                        results.Add(entity.NetworkId);
                    continue;
                }

                // Rule 4: Enemy in detection range of any allied source
                if (IsInAllyDetection(entity.Position, allySources))
                {
                    results.Add(entity.NetworkId);
                    continue;
                }

                // Rule 5: Enemy in a reveal zone belonging to our team
                if (IsInRevealZone(entity.Position, myTeamId))
                {
                    results.Add(entity.NetworkId);
                }
            }
        }

        public bool IsVisible(INetConnection connection, INetworkEntity entity)
        {
            if (entity.AlwaysRelevant) return true;
            if (!_connectionTeams.TryGetValue(connection.ConnectionId, out int myTeamId)) return false;

            _entityTeams.TryGetValue(entity.NetworkId, out int entityTeamId);
            if (entityTeamId == myTeamId) return true;

            bool isHidden = _hiddenEntities.Contains(entity.NetworkId);
            if (isHidden) return IsInDeepReveal(entity.Position, myTeamId);

            var allySources = _teamDetectionSources.TryGetValue(myTeamId, out var s) ? s : EmptyDetectionList;
            if (IsInAllyDetection(entity.Position, allySources)) return true;
            if (IsInRevealZone(entity.Position, myTeamId)) return true;

            return false;
        }

        // --- Internal ---

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsInAllyDetection(Vector3 targetPos, List<DetectionSource> sources)
        {
            for (int i = 0; i < sources.Count; i++)
            {
                float dx = targetPos.x - sources[i].Position.x;
                float dz = targetPos.z - sources[i].Position.z;
                if (dx * dx + dz * dz <= sources[i].RangeSqr)
                    return true;
            }
            return false;
        }

        private bool IsInRevealZone(Vector3 pos, int teamId)
        {
            for (int i = 0; i < _revealZones.Count; i++)
            {
                var zone = _revealZones[i];
                if (zone.TeamId != teamId) continue;
                float dx = pos.x - zone.Center.x;
                float dz = pos.z - zone.Center.z;
                if (dx * dx + dz * dz <= zone.RadiusSqr)
                    return true;
            }
            return false;
        }

        private bool IsInDeepReveal(Vector3 pos, int teamId)
        {
            for (int i = 0; i < _deepRevealZones.Count; i++)
            {
                var zone = _deepRevealZones[i];
                if (zone.TeamId != teamId) continue;
                float dx = pos.x - zone.Center.x;
                float dz = pos.z - zone.Center.z;
                if (dx * dx + dz * dz <= zone.RadiusSqr)
                    return true;
            }
            return false;
        }

        private static void CleanExpiredZones(List<RevealZone> list, float now)
        {
            for (int i = list.Count - 1; i >= 0; i--)
                if (now >= list[i].ExpireTime) list.RemoveAt(i);
        }

        private int _nextZoneId = 1;
        private const int MaxZoneId = int.MaxValue - 1;

        private struct DetectionSource
        {
            public Vector3 Position;
            public float RangeSqr;
        }

        private struct RevealZone
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
