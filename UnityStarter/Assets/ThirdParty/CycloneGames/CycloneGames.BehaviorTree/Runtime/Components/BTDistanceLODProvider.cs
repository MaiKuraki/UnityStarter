using System.Collections.Generic;
using UnityEngine;
using CycloneGames.BehaviorTree.Runtime.Core;

namespace CycloneGames.BehaviorTree.Runtime.Components
{
    public class BTDistanceLODProvider : MonoBehaviour, IBTLODProvider
    {
        [SerializeField] private BTLODConfig _config;
        [SerializeField] private Transform _referencePoint;

        // Parallel arrays for 0GC iteration (avoids Dictionary enumerator allocation)
        private RuntimeBehaviorTree[] _keys;
        private TreeLODData[] _values;
        private int _count;
        private int _capacity;

        // O(1) lookup index
        private readonly Dictionary<RuntimeBehaviorTree, int> _indexMap = new Dictionary<RuntimeBehaviorTree, int>();

        // Reusable buffer for external consumers
        private readonly List<RuntimeBehaviorTree> _iterBuffer = new List<RuntimeBehaviorTree>();

        private struct TreeLODData
        {
            public Transform Transform;
            public int CurrentPriority;
            public int CurrentTickInterval;
            public double BoostEndTime;
            public bool HasTypeOverride;
            public int TypePriority;
            public int TypeTickInterval;
            public bool HasGroupOverride;
            public int GroupId;
            public int GroupPriority;
            public int GroupTickInterval;
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
            const int INITIAL_CAPACITY = 64;
            _capacity = INITIAL_CAPACITY;
            _keys = new RuntimeBehaviorTree[_capacity];
            _values = new TreeLODData[_capacity];
            _count = 0;

            if (_referencePoint == null)
            {
                var player = GameObject.FindGameObjectWithTag("Player");
                if (player != null) _referencePoint = player.transform;
            }
        }

        public void RegisterTree(RuntimeBehaviorTree tree, Transform treeTransform)
        {
            if (tree == null || _indexMap.ContainsKey(tree)) return;

            if (_count >= _capacity)
            {
                int newCap = _capacity * 2;
                var newKeys = new RuntimeBehaviorTree[newCap];
                var newValues = new TreeLODData[newCap];
                System.Array.Copy(_keys, newKeys, _count);
                System.Array.Copy(_values, newValues, _count);
                _keys = newKeys;
                _values = newValues;
                _capacity = newCap;
            }

            var data = new TreeLODData
            {
                Transform = treeTransform,
                CurrentPriority = 0,
                CurrentTickInterval = 1,
                BoostEndTime = 0f,
                HasTypeOverride = false
            };

            if (_config != null && treeTransform != null)
            {
                var go = treeTransform.gameObject;
                if (_config.TryGetPriorityMarker(go, out int priority, out int interval))
                {
                    data.HasTypeOverride = true;
                    data.TypePriority = priority;
                    data.TypeTickInterval = interval;
                }

                var groupProvider = go.GetComponent<IBTAgentGroupProvider>();
                if (groupProvider != null)
                {
                    data.HasGroupOverride = true;
                    data.GroupId = groupProvider.GroupId;
                    data.GroupPriority = groupProvider.GroupPriority;
                    data.GroupTickInterval = groupProvider.GroupTickInterval;
                }
            }

            int idx = _count;
            _keys[idx] = tree;
            _values[idx] = data;
            _indexMap[tree] = idx;
            _count++;
        }

        public void UnregisterTree(RuntimeBehaviorTree tree)
        {
            if (!_indexMap.TryGetValue(tree, out int idx)) return;

            int last = _count - 1;
            if (idx != last)
            {
                _keys[idx] = _keys[last];
                _values[idx] = _values[last];
                _indexMap[_keys[idx]] = idx;
            }
            _keys[last] = null;
            _values[last] = default;
            _indexMap.Remove(tree);
            _count--;
        }

        public int GetPriority(RuntimeBehaviorTree tree)
        {
            if (!_indexMap.TryGetValue(tree, out int idx)) return 0;
            ref var data = ref _values[idx];

            if (RuntimeBTTime.GetUnityTime(false) < data.BoostEndTime && _config != null)
                return _config.BoostedPriority;

            if (data.HasTypeOverride && data.TypePriority >= 0)
                return data.TypePriority;

            if (data.HasGroupOverride && data.GroupPriority >= 0)
                return data.GroupPriority;

            return data.CurrentPriority;
        }

        public int GetTickInterval(RuntimeBehaviorTree tree)
        {
            if (!_indexMap.TryGetValue(tree, out int idx)) return 1;
            ref var data = ref _values[idx];

            if (RuntimeBTTime.GetUnityTime(false) < data.BoostEndTime && _config != null)
                return _config.BoostedTickInterval;

            if (data.HasTypeOverride && data.TypeTickInterval >= 0)
                return data.TypeTickInterval;

            if (data.HasGroupOverride && data.GroupTickInterval > 0)
                return data.GroupTickInterval;

            return data.CurrentTickInterval;
        }

        public bool TryGetGroupId(RuntimeBehaviorTree tree, out int groupId)
        {
            groupId = -1;
            if (!_indexMap.TryGetValue(tree, out int idx)) return false;
            ref var data = ref _values[idx];
            if (!data.HasGroupOverride) return false;
            groupId = data.GroupId;
            return true;
        }

        public void BoostPriority(RuntimeBehaviorTree tree, float duration)
        {
            if (!_indexMap.TryGetValue(tree, out int idx)) return;
            _values[idx].BoostEndTime = RuntimeBTTime.GetUnityTime(false) + duration;
        }

        public void UpdateLOD(RuntimeBehaviorTree tree)
        {
            if (_config == null || _referencePoint == null) return;
            if (!_indexMap.TryGetValue(tree, out int idx)) return;
            ref var data = ref _values[idx];
            if (data.Transform == null) return;

            float sqrDist = (_referencePoint.position - data.Transform.position).sqrMagnitude;
            int lodLevel = _config.GetLODLevelSqr(sqrDist);

            if (lodLevel >= 0 && lodLevel < _config.Levels.Length)
            {
                data.CurrentPriority = _config.Levels[lodLevel].Priority;
                data.CurrentTickInterval = _config.Levels[lodLevel].TickInterval;
            }
        }

        // 0GC: iterates parallel arrays directly, no enumerator allocation
        public void UpdateAllLOD()
        {
            if (_config == null || _referencePoint == null) return;

            var refPos = _referencePoint.position;
            for (int i = 0; i < _count; i++)
            {
                ref var data = ref _values[i];
                if (data.Transform == null) continue;

                float sqrDist = (refPos - data.Transform.position).sqrMagnitude;
                int lodLevel = _config.GetLODLevelSqr(sqrDist);

                if (lodLevel >= 0 && lodLevel < _config.Levels.Length)
                {
                    data.CurrentPriority = _config.Levels[lodLevel].Priority;
                    data.CurrentTickInterval = _config.Levels[lodLevel].TickInterval;
                }
            }
        }

        // 0GC: returns pre-allocated buffer filled from parallel arrays
        public List<RuntimeBehaviorTree> GetTreeBuffer()
        {
            _iterBuffer.Clear();
            for (int i = 0; i < _count; i++)
            {
                _iterBuffer.Add(_keys[i]);
            }
            return _iterBuffer;
        }

        public int Count => _count;
    }
}
