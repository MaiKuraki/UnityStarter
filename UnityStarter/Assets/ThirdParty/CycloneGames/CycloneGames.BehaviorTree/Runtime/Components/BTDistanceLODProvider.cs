using System.Collections.Generic;
using UnityEngine;
using CycloneGames.BehaviorTree.Runtime.Core;

namespace CycloneGames.BehaviorTree.Runtime.Components
{
    public class BTDistanceLODProvider : MonoBehaviour, IBTLODProvider
    {
        [SerializeField] private BTLODConfig _config;
        [SerializeField] private Transform _referencePoint;

        private readonly Dictionary<RuntimeBehaviorTree, TreeLODData> _treeData = new Dictionary<RuntimeBehaviorTree, TreeLODData>();

        private struct TreeLODData
        {
            public Transform Transform;
            public int CurrentPriority;
            public int CurrentTickInterval;
            public float BoostEndTime;
            public bool HasTypeOverride;
            public int TypePriority;
            public int TypeTickInterval;
        }

        public BTLODConfig Config
        {
            get => _config;
            set => _config = value;
        }

        public Transform ReferencePoint
        {
            get => _referencePoint;
            set => _referencePoint = value;
        }

        private void Awake()
        {
            if (_referencePoint == null)
            {
                var player = GameObject.FindGameObjectWithTag("Player");
                if (player != null) _referencePoint = player.transform;
            }
        }

        public void RegisterTree(RuntimeBehaviorTree tree, Transform treeTransform)
        {
            if (tree == null || _treeData.ContainsKey(tree)) return;

            var data = new TreeLODData
            {
                Transform = treeTransform,
                CurrentPriority = 0,
                CurrentTickInterval = 1,
                BoostEndTime = 0f,
                HasTypeOverride = false
            };

            // Check for priority marker (0GC, no reflection)
            if (_config != null && treeTransform != null)
            {
                var go = treeTransform.gameObject;
                if (_config.TryGetPriorityMarker(go, out int priority, out int interval))
                {
                    data.HasTypeOverride = true;
                    data.TypePriority = priority;
                    data.TypeTickInterval = interval;
                }
            }

            _treeData[tree] = data;
        }

        public void UnregisterTree(RuntimeBehaviorTree tree)
        {
            _treeData.Remove(tree);
        }

        public int GetPriority(RuntimeBehaviorTree tree)
        {
            if (!_treeData.TryGetValue(tree, out var data)) return 0;

            // Boost takes precedence
            if (Time.time < data.BoostEndTime && _config != null)
                return _config.BoostedPriority;

            // Type override takes precedence over distance
            if (data.HasTypeOverride && data.TypePriority >= 0)
                return data.TypePriority;

            return data.CurrentPriority;
        }

        public int GetTickInterval(RuntimeBehaviorTree tree)
        {
            if (!_treeData.TryGetValue(tree, out var data)) return 1;

            // Boost takes precedence
            if (Time.time < data.BoostEndTime && _config != null)
                return _config.BoostedTickInterval;

            // Type override takes precedence over distance
            if (data.HasTypeOverride && data.TypeTickInterval >= 0)
                return data.TypeTickInterval;

            return data.CurrentTickInterval;
        }

        public void BoostPriority(RuntimeBehaviorTree tree, float duration)
        {
            if (!_treeData.TryGetValue(tree, out var data)) return;

            data.BoostEndTime = Time.time + duration;
            _treeData[tree] = data;
        }

        public void UpdateLOD(RuntimeBehaviorTree tree)
        {
            if (_config == null || _referencePoint == null) return;
            if (!_treeData.TryGetValue(tree, out var data)) return;
            if (data.Transform == null) return;

            float distance = Vector3.Distance(_referencePoint.position, data.Transform.position);
            int lodLevel = _config.GetLODLevel(distance);

            if (lodLevel >= 0 && lodLevel < _config.Levels.Length)
            {
                data.CurrentPriority = _config.Levels[lodLevel].Priority;
                data.CurrentTickInterval = _config.Levels[lodLevel].TickInterval;
                _treeData[tree] = data;
            }
        }

        public void UpdateAllLOD()
        {
            if (_config == null || _referencePoint == null) return;

            var keys = new List<RuntimeBehaviorTree>(_treeData.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                UpdateLOD(keys[i]);
            }
        }
    }
}
