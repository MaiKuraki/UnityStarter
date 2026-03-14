using System;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Core
{
    public interface IBTPriorityMarker
    {
        int Priority { get; }
        int TickInterval { get; }
    }

    [CreateAssetMenu(fileName = "BTLODConfig", menuName = "CycloneGames/AI/BT LOD Config")]
    public class BTLODConfig : ScriptableObject
    {
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
            if (Levels == null) return;
            _sqrDistances = new float[Levels.Length];
            for (int i = 0; i < Levels.Length; i++)
            {
                float d = Levels[i].MaxDistance;
                _sqrDistances[i] = (d >= float.MaxValue / 2f) ? float.MaxValue : d * d;
            }
        }

        public int GetLODLevel(float distance)
        {
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
                priority = marker.Priority;
                tickInterval = marker.TickInterval;
                return true;
            }
            return false;
        }
    }
}
