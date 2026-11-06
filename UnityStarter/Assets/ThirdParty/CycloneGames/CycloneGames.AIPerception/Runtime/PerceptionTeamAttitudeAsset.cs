using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace CycloneGames.AIPerception.Runtime
{
    /// <summary>
    /// Authoring container for team attitudes.
    /// </summary>
    /// <remarks>
    /// Serialized as a relation list plus a default policy. The dense 256x256 lookup table is rebuilt
    /// from that list on demand and never serialized.
    /// </remarks>
    [CreateAssetMenu(
        menuName = "CycloneGames/AI Perception/Team Attitude Table",
        fileName = "PerceptionTeamAttitudeTable")]
    public sealed class PerceptionTeamAttitudeAsset : ScriptableObject
    {
        [Serializable]
        public struct Relation
        {
            public int ObserverTeam;
            public int TargetTeam;
            public PerceptionTeamAttitude Attitude;

            public Relation(int observerTeam, int targetTeam, PerceptionTeamAttitude attitude)
            {
                ObserverTeam = observerTeam;
                TargetTeam = targetTeam;
                Attitude = attitude;
            }
        }

        [SerializeField] private PerceptionTeamAttitude _defaultAttitude = PerceptionTeamAttitude.Neutral;
        [SerializeField] private bool _sameTeamFriendly;
        [SerializeField] private Relation[] _relations = Array.Empty<Relation>();

        [NonSerialized] private byte[] _cache;
        [NonSerialized] private bool _cacheDirty = true;
        [NonSerialized] private int _version;

        /// <summary>Attitude used for every pair that no relation overrides.</summary>
        /// <summary>
        /// Increments on every mutation. Editors poll it to repaint only when the data actually changed.
        /// </summary>
        public int Version => _version;

        public PerceptionTeamAttitude DefaultAttitude => _defaultAttitude;

        /// <summary>When set, a team is friendly with itself unless a relation overrides it.</summary>
        public bool SameTeamFriendly => _sameTeamFriendly;

        /// <summary>Number of explicit overrides. This is what gets serialized.</summary>
        public int RelationCount => _relations?.Length ?? 0;

        /// <summary>Read-only view of the overrides, for tooling and inspection.</summary>
        public IReadOnlyList<Relation> Relations => _relations ?? Array.Empty<Relation>();

        public PerceptionTeamAttitude GetAttitude(int observerTeam, int targetTeam)
        {
            if (!IsTeamValid(observerTeam) || !IsTeamValid(targetTeam))
            {
                return PerceptionTeamAttitude.Neutral;
            }

            EnsureCache();
            return (PerceptionTeamAttitude)_cache[PerceptionTeam.AttitudeIndex(observerTeam, targetTeam)];
        }

        /// <summary>
        /// Overrides one pair. Writing the value the policy already produces removes the override
        /// instead of storing a redundant relation.
        /// </summary>
        public void SetAttitude(int observerTeam, int targetTeam, PerceptionTeamAttitude attitude)
        {
            if (!IsTeamValid(observerTeam) || !IsTeamValid(targetTeam))
            {
                throw new ArgumentOutOfRangeException(nameof(observerTeam));
            }

            EnsureCache();
            _cache[PerceptionTeam.AttitudeIndex(observerTeam, targetTeam)] = (byte)attitude;
            _version++;

            _relations ??= Array.Empty<Relation>();
            int existing = IndexOfRelation(observerTeam, targetTeam);
            bool matchesPolicy = attitude == PolicyAttitude(observerTeam, targetTeam);

            if (matchesPolicy)
            {
                if (existing >= 0)
                {
                    RemoveRelationAt(existing);
                }

                return;
            }

            if (existing >= 0)
            {
                Relation relation = _relations[existing];
                relation.Attitude = attitude;
                _relations[existing] = relation;
                return;
            }

            AppendRelation(new Relation(observerTeam, targetTeam, attitude));
        }

        public void SetSameTeamFriendly(bool enabled)
        {
            if (_sameTeamFriendly == enabled)
            {
                return;
            }

            _sameTeamFriendly = enabled;
            _version++;
            Normalize();
        }

        public void SetSameTeamFriendly()
        {
            SetSameTeamFriendly(true);
        }

        /// <summary>Clears every override and installs a fresh policy.</summary>
        public void ResetTo(PerceptionTeamAttitude defaultAttitude, bool sameTeamFriendly)
        {
            _defaultAttitude = defaultAttitude;
            _sameTeamFriendly = sameTeamFriendly;
            _relations = Array.Empty<Relation>();
            _cacheDirty = true;
            _version++;
        }

        /// <summary>
        /// Sets the default policy. Overrides that no longer differ from it are dropped, the rest stay.
        /// </summary>
        public void SetDefaultAttitude(PerceptionTeamAttitude attitude)
        {
            if (_defaultAttitude == attitude && (_relations?.Length ?? 0) == 0)
            {
                return;
            }

            _defaultAttitude = attitude;
            _version++;
            Normalize();
        }

        /// <summary>Overwrites the manager table with this asset in one copy.</summary>
        public void ApplyTo(SensorManager manager)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }

            Normalize();
            EnsureCache();
            var buffer = new NativeArray<byte>(PerceptionTeam.AttitudeTableSize, Allocator.Temp);
            try
            {
                buffer.CopyFrom(_cache);
                manager.CopyTeamAttitudes(buffer);
            }
            finally
            {
                buffer.Dispose();
            }
        }

        /// <summary>
        /// Captures a manager table back into a minimal relation list. Cells that already match the
        /// policy are dropped, so a typical two-faction setup serializes as a handful of rows.
        /// </summary>
        public void CaptureFrom(SensorManager manager)
        {
            if (manager == null)
            {
                throw new ArgumentNullException(nameof(manager));
            }

            EnsureCache();
            manager.CopyTeamAttitudesTo(_cache);
            AdoptPolicyFromCache();

            var relations = new List<Relation>();
            for (int observer = 0; observer < PerceptionTeam.TeamCount; observer++)
            {
                for (int target = 0; target < PerceptionTeam.TeamCount; target++)
                {
                    var attitude = (PerceptionTeamAttitude)_cache[PerceptionTeam.AttitudeIndex(observer, target)];
                    if (attitude != PolicyAttitude(observer, target))
                    {
                        relations.Add(new Relation(observer, target, attitude));
                    }
                }
            }

            _relations = relations.ToArray();
            _cacheDirty = false;
            _version++;
        }

        /// <summary>
        /// Infers the policy from the captured table so the relation list stays small: a uniform
        /// diagonal becomes <see cref="SameTeamFriendly"/>, and a uniform off-diagonal block becomes
        /// the default attitude. Without this a two-faction table would serialize 256 redundant rows.
        /// </summary>
        private void AdoptPolicyFromCache()
        {
            _sameTeamFriendly = true;
            for (int team = 0; team < PerceptionTeam.TeamCount; team++)
            {
                if (_cache[PerceptionTeam.AttitudeIndex(team, team)] != (byte)PerceptionTeamAttitude.Friendly)
                {
                    _sameTeamFriendly = false;
                    break;
                }
            }

            var counts = new int[3];
            for (int observer = 0; observer < PerceptionTeam.TeamCount; observer++)
            {
                for (int target = 0; target < PerceptionTeam.TeamCount; target++)
                {
                    if (observer == target)
                    {
                        continue;
                    }

                    counts[_cache[PerceptionTeam.AttitudeIndex(observer, target)]]++;
                }
            }

            int offDiagonal = PerceptionTeam.TeamCount * (PerceptionTeam.TeamCount - 1);
            _defaultAttitude = PerceptionTeamAttitude.Neutral;
            for (int attitude = 0; attitude < counts.Length; attitude++)
            {
                if (counts[attitude] == offDiagonal)
                {
                    _defaultAttitude = (PerceptionTeamAttitude)attitude;
                    break;
                }
            }

            _cacheDirty = false;
        }

        /// <summary>
        /// Repairs the relation list in place: drops out-of-range teams, keeps only the last entry per
        /// pair, removes entries that the policy already produces, and sorts the result so the same
        /// table always serializes identically. Safe to call repeatedly.
        /// </summary>
        public void Normalize()
        {
            if (_relations == null || _relations.Length == 0)
            {
                _relations = Array.Empty<Relation>();
                _cacheDirty = true;
                return;
            }

            var byPair = new Dictionary<int, PerceptionTeamAttitude>(_relations.Length);
            for (int i = 0; i < _relations.Length; i++)
            {
                Relation relation = _relations[i];
                if (!IsTeamValid(relation.ObserverTeam) || !IsTeamValid(relation.TargetTeam))
                {
                    continue;
                }

                // Later entries win, matching how the cache is built.
                byPair[PerceptionTeam.AttitudeIndex(relation.ObserverTeam, relation.TargetTeam)] = relation.Attitude;
            }

            var kept = new List<Relation>(byPair.Count);
            for (int index = 0; index < PerceptionTeam.AttitudeTableSize; index++)
            {
                if (!byPair.TryGetValue(index, out PerceptionTeamAttitude attitude))
                {
                    continue;
                }

                int observer = index / PerceptionTeam.TeamCount;
                int target = index % PerceptionTeam.TeamCount;
                if (attitude == PolicyAttitude(observer, target))
                {
                    continue;
                }

                kept.Add(new Relation(observer, target, attitude));
            }

            kept.Sort(static (left, right) =>
                left.ObserverTeam != right.ObserverTeam
                    ? left.ObserverTeam.CompareTo(right.ObserverTeam)
                    : left.TargetTeam.CompareTo(right.TargetTeam));

            _relations = kept.Count == 0 ? Array.Empty<Relation>() : kept.ToArray();
            _cacheDirty = true;
            _version++;
        }

        private PerceptionTeamAttitude PolicyAttitude(int observerTeam, int targetTeam)
        {
            if (_sameTeamFriendly && observerTeam == targetTeam)
            {
                return PerceptionTeamAttitude.Friendly;
            }

            return _defaultAttitude;
        }

        private void EnsureCache()
        {
            if (!_cacheDirty && _cache != null && _cache.Length == PerceptionTeam.AttitudeTableSize)
            {
                return;
            }

            if (_cache == null || _cache.Length != PerceptionTeam.AttitudeTableSize)
            {
                _cache = new byte[PerceptionTeam.AttitudeTableSize];
            }

            byte policy = (byte)_defaultAttitude;
            for (int i = 0; i < _cache.Length; i++)
            {
                _cache[i] = policy;
            }

            if (_sameTeamFriendly)
            {
                for (int team = 0; team < PerceptionTeam.TeamCount; team++)
                {
                    _cache[PerceptionTeam.AttitudeIndex(team, team)] = (byte)PerceptionTeamAttitude.Friendly;
                }
            }

            if (_relations != null)
            {
                for (int i = 0; i < _relations.Length; i++)
                {
                    Relation relation = _relations[i];
                    if (IsTeamValid(relation.ObserverTeam) && IsTeamValid(relation.TargetTeam))
                    {
                        _cache[PerceptionTeam.AttitudeIndex(relation.ObserverTeam, relation.TargetTeam)] =
                            (byte)relation.Attitude;
                    }
                }
            }

            _cacheDirty = false;
        }

        private int IndexOfRelation(int observerTeam, int targetTeam)
        {
            if (_relations == null)
            {
                return -1;
            }

            for (int i = 0; i < _relations.Length; i++)
            {
                if (_relations[i].ObserverTeam == observerTeam && _relations[i].TargetTeam == targetTeam)
                {
                    return i;
                }
            }

            return -1;
        }

        private void AppendRelation(Relation relation)
        {
            int length = _relations.Length;
            Array.Resize(ref _relations, length + 1);
            _relations[length] = relation;
        }

        private void RemoveRelationAt(int index)
        {
            int last = _relations.Length - 1;
            _relations[index] = _relations[last];
            Array.Resize(ref _relations, last);
        }

        private static bool IsTeamValid(int team)
        {
            return team >= 0 && team < PerceptionTeam.TeamCount;
        }

        private void OnValidate()
        {
            Normalize();
        }
    }
}
