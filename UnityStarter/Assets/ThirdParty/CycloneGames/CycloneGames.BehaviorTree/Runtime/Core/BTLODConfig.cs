using System;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Core
{
    public interface IBTPriorityMarker
    {
        int Priority { get; }
        int TickInterval { get; }
    }

    public interface IBTAgentGroupProvider
    {
        int GroupId { get; }
        int GroupPriority { get; }
        int GroupTickInterval { get; }
    }

    [CreateAssetMenu(fileName = "BTLODConfig", menuName = "CycloneGames/AI/BT LOD Config")]
    public class BTLODConfig : ScriptableObject
    {
        public const int MaxPriorityLevels = 8;

        [Serializable]
        public struct LODLevel
        {
            [Tooltip("Max distance for this LOD level")]
            public float MaxDistance;

            [Tooltip("Tick every N frames")]
            public int TickInterval;

            [Tooltip("Priority bucket (0 = highest)")]
            public int Priority;
        }

        [Header("Distance-Based LOD")]
        [Tooltip("LOD levels sorted by distance")]
        public LODLevel[] Levels = new LODLevel[]
        {
            new LODLevel { MaxDistance = 10f, TickInterval = 1, Priority = 0 },
            new LODLevel { MaxDistance = 30f, TickInterval = 2, Priority = 1 },
            new LODLevel { MaxDistance = 50f, TickInterval = 4, Priority = 2 },
            new LODLevel { MaxDistance = float.MaxValue, TickInterval = 8, Priority = 3 }
        };

        [Header("Budget Settings")]
        [Tooltip("Max trees to tick per priority level per frame")]
        public int[] PriorityBudgets = new int[] { 100, 50, 30, 20 };

        [Header("Boost Settings")]
        [Tooltip("Priority when boosted (e.g., when attacked)")]
        public int BoostedPriority = 0;

        [Tooltip("Tick interval when boosted")]
        public int BoostedTickInterval = 1;

        // Pre-squared distances for 0GC LOD checks
        private float[] _sqrDistances;
        private bool _isValid;

        public bool IsValid => _isValid;

        private void OnEnable()
        {
            RebuildSqrDistances();
        }

        private void OnValidate()
        {
            RebuildSqrDistances();
        }

        private void RebuildSqrDistances()
        {
            _isValid = TryValidate(out _);
            if (!_isValid)
            {
                _sqrDistances = Array.Empty<float>();
                return;
            }

            _sqrDistances = new float[Levels.Length];
            for (int i = 0; i < Levels.Length; i++)
            {
                float d = Levels[i].MaxDistance;
                _sqrDistances[i] = (d >= float.MaxValue / 2f) ? float.MaxValue : d * d;
            }
        }

        public int GetLODLevel(float distance)
        {
            if (!_isValid || float.IsNaN(distance) || distance < 0f)
            {
                return -1;
            }

            for (int i = 0; i < Levels.Length; i++)
            {
                if (distance <= Levels[i].MaxDistance)
                    return i;
            }
            return Levels.Length - 1;
        }

        // sqrMagnitude-based LOD check avoids sqrt per agent per LOD update
        public int GetLODLevelSqr(float sqrDistance)
        {
            if (_sqrDistances == null) RebuildSqrDistances();
            if (!_isValid || float.IsNaN(sqrDistance) || sqrDistance < 0f)
            {
                return -1;
            }

            for (int i = 0; i < _sqrDistances.Length; i++)
            {
                if (sqrDistance <= _sqrDistances[i])
                    return i;
            }
            return Levels.Length - 1;
        }

        public bool TryGetPriorityMarker(GameObject owner, out int priority, out int tickInterval)
        {
            priority = -1;
            tickInterval = -1;

            if (owner == null) return false;

            var marker = owner.GetComponent<IBTPriorityMarker>();
            if (marker != null)
            {
                if (marker.Priority < 0
                    || marker.Priority >= MaxPriorityLevels
                    || marker.TickInterval < 1)
                {
                    return false;
                }

                priority = marker.Priority;
                tickInterval = marker.TickInterval;
                return true;
            }
            return false;
        }

        public bool TryValidate(out string error)
        {
            if (Levels == null || Levels.Length == 0)
            {
                error = "LOD Levels must contain at least one entry.";
                return false;
            }

            if (Levels.Length > MaxPriorityLevels)
            {
                error = $"LOD Levels cannot exceed {MaxPriorityLevels} entries.";
                return false;
            }

            float previousDistance = -1f;
            int highestUsedPriority = BoostedPriority;
            for (int i = 0; i < Levels.Length; i++)
            {
                LODLevel level = Levels[i];
                if (float.IsNaN(level.MaxDistance)
                    || float.IsInfinity(level.MaxDistance)
                    || level.MaxDistance < 0f
                    || level.MaxDistance <= previousDistance)
                {
                    error = $"LOD Levels[{i}].MaxDistance must be finite or float.MaxValue, non-negative, and strictly increasing.";
                    return false;
                }

                if (level.TickInterval < 1)
                {
                    error = $"LOD Levels[{i}].TickInterval must be at least 1.";
                    return false;
                }

                if (level.Priority < 0 || level.Priority >= MaxPriorityLevels)
                {
                    error = $"LOD Levels[{i}].Priority must be between 0 and {MaxPriorityLevels - 1}.";
                    return false;
                }

                highestUsedPriority = Math.Max(highestUsedPriority, level.Priority);

                previousDistance = level.MaxDistance;
            }

            if (PriorityBudgets == null || PriorityBudgets.Length == 0 || PriorityBudgets.Length > MaxPriorityLevels)
            {
                error = $"PriorityBudgets must contain between 1 and {MaxPriorityLevels} entries.";
                return false;
            }

            for (int i = 0; i < PriorityBudgets.Length; i++)
            {
                if (PriorityBudgets[i] < 0)
                {
                    error = $"PriorityBudgets[{i}] cannot be negative.";
                    return false;
                }
            }

            if (PriorityBudgets.Length <= highestUsedPriority)
            {
                error = $"PriorityBudgets must define every used priority from 0 through {highestUsedPriority}.";
                return false;
            }

            if (BoostedPriority < 0 || BoostedPriority >= MaxPriorityLevels)
            {
                error = $"BoostedPriority must be between 0 and {MaxPriorityLevels - 1}.";
                return false;
            }

            if (BoostedTickInterval < 1)
            {
                error = "BoostedTickInterval must be at least 1.";
                return false;
            }

            error = null;
            return true;
        }
    }
}
