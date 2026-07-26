using System.Collections.Generic;
using UnityEngine;
using CycloneGames.BehaviorTree.Runtime.Core;

namespace CycloneGames.BehaviorTree.Runtime.Components
{
    [DisallowMultipleComponent]
    public class BTDistanceLODProvider : MonoBehaviour, IBTLODProvider
    {
        public const int DefaultMaximumTreeCount = 65_536;
        public const int HardMaximumTreeCount = 1_048_576;

        [SerializeField] private BTLODConfig _config;
        [SerializeField] private Transform _referencePoint;
        [SerializeField, Min(1)] private int _maximumTreeCount = DefaultMaximumTreeCount;

        // Parallel arrays for 0GC iteration (avoids Dictionary enumerator allocation)
        private RuntimeBehaviorTree[] _keys;
        private TreeLODData[] _values;
        private int _count;
        private int _capacity;
        private int _peakTreeCount;
        private long _capacityRejectedTreeCount;
        private bool _legacyRegistrationCapacityReported;

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

        public int MaximumTreeCount
        {
            get => _maximumTreeCount;
            set
            {
                if (value < 1 || value > HardMaximumTreeCount)
                {
                    throw new System.ArgumentOutOfRangeException(nameof(value));
                }

                if (_count != 0)
                {
                    throw new System.InvalidOperationException(
                        "LOD provider capacity cannot change while trees are registered.");
                }

                _maximumTreeCount = value;
                if (_keys != null && _capacity > value)
                {
                    _keys = new RuntimeBehaviorTree[value];
                    _values = new TreeLODData[value];
                    _capacity = value;
                }
            }
        }

        private void Awake()
        {
            EnsureInitialized();
        }

        internal void EnsureInitialized()
        {
            if (_keys != null)
            {
                return;
            }

            _maximumTreeCount = Mathf.Clamp(_maximumTreeCount, 1, HardMaximumTreeCount);
            const int INITIAL_CAPACITY = 64;
            _capacity = Mathf.Min(INITIAL_CAPACITY, _maximumTreeCount);
            _keys = new RuntimeBehaviorTree[_capacity];
            _values = new TreeLODData[_capacity];
            _count = 0;
            _indexMap.Clear();
            _iterBuffer.Clear();
        }

        public void RegisterTree(RuntimeBehaviorTree tree, Transform treeTransform)
        {
            long rejectedBefore = _capacityRejectedTreeCount;
            if (TryRegisterTree(tree, treeTransform) ||
                _legacyRegistrationCapacityReported ||
                _capacityRejectedTreeCount <= rejectedBefore)
            {
                return;
            }

            _legacyRegistrationCapacityReported = true;
            Debug.LogError(
                $"[BTDistanceLODProvider] Legacy RegisterTree was rejected because LOD tree capacity " +
                $"was exhausted on '{gameObject.name}'. Use TryRegisterTree to handle admission failure.",
                this);
        }

        public bool TryRegisterTree(RuntimeBehaviorTree tree, Transform treeTransform)
        {
            if (tree == null) return false;
            if (_indexMap.ContainsKey(tree)) return true;

            EnsureInitialized();

            if (_count >= _maximumTreeCount)
            {
                _capacityRejectedTreeCount++;
                return false;
            }

            if (_count >= _capacity)
            {
                int newCap = Mathf.Min(
                    _maximumTreeCount,
                    _capacity <= _maximumTreeCount / 2 ? _capacity * 2 : _maximumTreeCount);
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
            if (_count > _peakTreeCount)
            {
                _peakTreeCount = _count;
            }

            return true;
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

        public bool ContainsTree(RuntimeBehaviorTree tree)
        {
            return tree != null && _indexMap.ContainsKey(tree);
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

        public BTDistanceLODProviderMemoryStats GetMemoryStats()
        {
            EnsureInitialized();
            return new BTDistanceLODProviderMemoryStats(
                _count,
                _capacity,
                _maximumTreeCount,
                _peakTreeCount,
                _capacityRejectedTreeCount);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            _maximumTreeCount = Mathf.Clamp(_maximumTreeCount, 1, HardMaximumTreeCount);
        }
#endif
    }
}
