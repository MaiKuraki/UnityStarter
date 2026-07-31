using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using CycloneGames.Logging;
using UnityEngine;
using CycloneGames.BehaviorTree.Runtime.Core;

namespace CycloneGames.BehaviorTree.Runtime.Components
{
    [DisallowMultipleComponent]
    public class BTPriorityTickManagerComponent : MonoBehaviour
    {
        private static readonly LogChannel Log = BehaviorTreeRuntimeLog.Channel;

        public const int DefaultPendingWakeUpCapacity = 4096;
        public const int MaximumPendingWakeUpCapacity = 65536;

        private static BTPriorityTickManagerComponent _instance;
        private static bool _isQuitting;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            _instance = null;
            _isQuitting = false;
        }

        public static BTPriorityTickManagerComponent Instance
        {
            get
            {
                if (_isQuitting) return null;

                if (_instance == null)
                {
                    _instance = BTManagerSceneResolver.FindExisting<BTPriorityTickManagerComponent>(nameof(BTPriorityTickManagerComponent));
                    if (_instance == null)
                    {
                        var go = new GameObject("[BTPriorityTickManager]");
                        BTPriorityTickManagerComponent created = go.AddComponent<BTPriorityTickManagerComponent>();
                        if (_instance == null)
                        {
                            _instance = created;
                        }
                        DontDestroyOnLoad(go);
                    }

                    _instance.PrepareForUse();
                }
                return _instance;
            }
        }

        public static bool HasInstance => _instance != null;

        [Header("Configuration")]
        [SerializeField] private BTLODConfig _config;
        [SerializeField] private float _lodUpdateInterval = 0.5f;
        [SerializeField, Min(1)] private int _maximumTreeCount = BTPriorityTickManager.DefaultMaximumTreeCount;
        [SerializeField, Min(1)] private int _pendingWakeUpCapacity = DefaultPendingWakeUpCapacity;

        [Header("Reference Point")]
        [SerializeField] private Transform _referencePoint;
        [SerializeField] private string _playerTag = "Player";
        [SerializeField] private bool _autoFindPlayer = true;

        private BTPriorityTickManager _manager;
        private BTDistanceLODProvider _lodProvider;
        private readonly object _pendingWakeUpGate = new object();
        private readonly Queue<RuntimeBehaviorTree> _pendingWakeUps = new Queue<RuntimeBehaviorTree>(64);
        private readonly HashSet<RuntimeBehaviorTree> _pendingWakeUpSet =
            new HashSet<RuntimeBehaviorTree>(RuntimeBehaviorTreeReferenceComparer.Instance);
        private double _lastLODUpdateTime;
        private bool _initialized;
        private int _acceptWakeUps;
        private int _pendingWakeUpPeak;
        private long _acceptedWakeUpCount;
        private long _coalescedWakeUpCount;
        private long _capacityRejectedWakeUpCount;
        private string _lastConfigError;
        private bool _legacyRegistrationCapacityReported;

#if UNITY_EDITOR
        [Header("Debug Stats (Editor Only)")]
        [SerializeField] private int _totalTreeCount;
        [SerializeField] private int[] _priorityTreeCounts = new int[8];
#endif

        public BTLODConfig Config
        {
            get => _config;
            set
            {
                _config = value;
                ApplyConfig();
            }
        }

        public Transform ReferencePoint
        {
            get => _referencePoint;
            set => SetReferencePoint(value);
        }

        public int TotalTreeCount => _manager?.GetTotalCount() ?? 0;
        public int PendingWakeUpCapacity
        {
            get
            {
                lock (_pendingWakeUpGate)
                {
                    return _pendingWakeUpCapacity;
                }
            }
            set
            {
                ValidatePendingWakeUpCapacity(value);
                lock (_pendingWakeUpGate)
                {
                    if (_pendingWakeUps.Count != 0)
                    {
                        throw new InvalidOperationException(
                            "Pending wake-up capacity cannot change while wake-ups are queued.");
                    }

                    _pendingWakeUpCapacity = value;
                }
            }
        }

        public float LODUpdateInterval
        {
            get => _lodUpdateInterval;
            set => _lodUpdateInterval = Mathf.Max(0.1f, value);
        }

        private void Awake()
        {
            if (_instance == null)
            {
                _instance = BTManagerSceneResolver.FindExisting<BTPriorityTickManagerComponent>(nameof(BTPriorityTickManagerComponent));
            }

            if (_instance != null && _instance != this)
            {
                Log.Warning(
                    $"[BTPriorityTickManagerComponent] Removing duplicate manager component from '{gameObject.name}'. " +
                    $"The active instance is '{_instance.gameObject.name}'.");
                Destroy(this);
                return;
            }
            _instance = this;
            PrepareForUse();
        }

        private void OnApplicationQuit()
        {
            _isQuitting = true;
        }

        private void Initialize()
        {
            if (_initialized) return;

            if (_pendingWakeUpCapacity < 1 || _pendingWakeUpCapacity > MaximumPendingWakeUpCapacity)
            {
                _pendingWakeUpCapacity = DefaultPendingWakeUpCapacity;
            }

            _maximumTreeCount = Mathf.Clamp(
                _maximumTreeCount,
                1,
                BTPriorityTickManager.HardMaximumTreeCount);

            int[] budgets = _config != null && _config.TryValidate(out _)
                ? _config.PriorityBudgets
                : null;
            int initialBucketCapacity = Mathf.Min(
                BTPriorityTickManager.DefaultInitialBucketCapacity,
                _maximumTreeCount);
            _manager = new BTPriorityTickManager(
                budgets,
                initialBucketCapacity,
                _maximumTreeCount,
                _maximumTreeCount);

            if (!TryGetComponent(out _lodProvider))
            {
                _lodProvider = gameObject.AddComponent<BTDistanceLODProvider>();
            }
            _lodProvider.EnsureInitialized();
            _lodProvider.MaximumTreeCount = _maximumTreeCount;
            ApplyConfig();

            if (_autoFindPlayer && _referencePoint == null)
            {
                TryFindPlayer();
            }

            _initialized = true;
        }

        private void PrepareForUse()
        {
            Volatile.Write(ref _acceptWakeUps, 1);
            Initialize();
        }

        private void ApplyConfig()
        {
            if (_config != null && !_config.TryValidate(out string configError))
            {
                if (!string.Equals(_lastConfigError, configError, System.StringComparison.Ordinal))
                {
                    _lastConfigError = configError;
                    Log.Error($"Invalid BTLODConfig '{_config.name}': {configError}");
                }
                return;
            }

            _lastConfigError = null;
            if (_manager != null && _config != null)
            {
                _manager.SetBudgets(_config.PriorityBudgets);
            }
            if (_lodProvider != null)
            {
                _lodProvider.Config = _config;
            }
        }

        private void TryFindPlayer()
        {
            if (string.IsNullOrEmpty(_playerTag)) return;

            GameObject player;
            try
            {
                player = GameObject.FindGameObjectWithTag(_playerTag);
            }
            catch (UnityException exception)
            {
                _autoFindPlayer = false;
                Log.Error(
                    exception,
                    $"Player tag '{_playerTag}' is not defined. Automatic lookup was disabled.");
                return;
            }

            if (player != null)
            {
                _referencePoint = player.transform;
                if (_lodProvider != null)
                {
                    _lodProvider.ReferencePoint = _referencePoint;
                }
            }
        }

        private void Update()
        {
            if (!_initialized) return;

            double currentTime = RuntimeBTTime.GetUnityTime(false);
            if (currentTime - _lastLODUpdateTime >= _lodUpdateInterval)
            {
                if (_autoFindPlayer && _referencePoint == null)
                {
                    TryFindPlayer();
                }

                if (_lodProvider != null)
                {
                    _lodProvider.UpdateAllLOD();
                    UpdateAllPriorities();
                }
                _lastLODUpdateTime = currentTime;

#if UNITY_EDITOR
                UpdateDebugStats();
#endif
            }

            PromoteWakeUpTrees();
            _manager?.Tick();
        }

        private void OnDestroy()
        {
            Volatile.Write(ref _acceptWakeUps, 0);
            if (_lodProvider != null)
            {
                var trees = _lodProvider.GetTreeBuffer();
                for (int i = 0; i < trees.Count; i++)
                {
                    if (trees[i] != null)
                    {
                        trees[i].WakeUpRequested -= EnqueueWakeUp;
                        _lodProvider.UnregisterTree(trees[i]);
                    }
                }
            }

            lock (_pendingWakeUpGate)
            {
                _pendingWakeUps.Clear();
                _pendingWakeUpSet.Clear();
            }

            _manager?.Clear();
            if (_instance == this) _instance = null;
        }

        public void Register(RuntimeBehaviorTree tree, Transform treeTransform)
        {
            BTPriorityTickManagerMemoryStats before = GetMemoryStats();
            if (TryRegister(tree, treeTransform) || _legacyRegistrationCapacityReported)
            {
                return;
            }

            BTPriorityTickManagerMemoryStats after = GetMemoryStats();
            bool coreCapacityRejected =
                after.Core.CapacityRejectedTreeCount > before.Core.CapacityRejectedTreeCount ||
                after.Core.CapacityRejectedMutationCount > before.Core.CapacityRejectedMutationCount;
            bool lodCapacityRejected =
                after.LOD.CapacityRejectedTreeCount > before.LOD.CapacityRejectedTreeCount;
            if (!coreCapacityRejected && !lodCapacityRejected)
            {
                return;
            }

            _legacyRegistrationCapacityReported = true;
            Log.Error(
                $"[BTPriorityTickManagerComponent] Legacy Register was rejected because managed tree, " +
                $"LOD, or deferred-mutation capacity was exhausted on '{gameObject.name}'. " +
                "Use TryRegister to handle admission failure.");
        }

        public bool TryRegister(RuntimeBehaviorTree tree, Transform treeTransform)
        {
            if (tree == null) return false;
            if (!_initialized) PrepareForUse();
            if (_lodProvider.ContainsTree(tree)) return true;

            if (!_lodProvider.TryRegisterTree(tree, treeTransform))
            {
                return false;
            }
            tree.WakeUpRequested += EnqueueWakeUp;
            _lodProvider.UpdateLOD(tree);

            int priority = _lodProvider.GetPriority(tree);
            tree.TickInterval = _lodProvider.GetTickInterval(tree);

            if (_manager.TryRegister(tree, priority))
            {
                return true;
            }

            tree.WakeUpRequested -= EnqueueWakeUp;
            _lodProvider.UnregisterTree(tree);
            return false;
        }

        public void Unregister(RuntimeBehaviorTree tree)
        {
            if (tree == null) return;
            tree.WakeUpRequested -= EnqueueWakeUp;
            _lodProvider?.UnregisterTree(tree);
            _manager?.Unregister(tree);
        }

        public void BoostPriority(RuntimeBehaviorTree tree, float duration)
        {
            if (tree == null || _lodProvider == null || duration <= 0f) return;

            _lodProvider.BoostPriority(tree, duration);

            if (_config != null && _config.IsValid && _manager != null)
            {
                _manager.UpdatePriority(tree, _config.BoostedPriority);
                tree.TickInterval = _config.BoostedTickInterval;
            }
        }

        public void SetReferencePoint(Transform target)
        {
            _referencePoint = target;
            if (_lodProvider != null)
            {
                _lodProvider.ReferencePoint = target;
            }
        }

        private void UpdateAllPriorities()
        {
            if (_lodProvider == null || _manager == null) return;

            var trees = _lodProvider.GetTreeBuffer();
            for (int i = 0; i < trees.Count; i++)
            {
                var tree = trees[i];
                int priority = _lodProvider.GetPriority(tree);
                int interval = _lodProvider.GetTickInterval(tree);
                _manager.UpdatePriority(tree, priority);
                tree.TickInterval = interval;
            }
        }

        private void EnqueueWakeUp(RuntimeBehaviorTree tree)
        {
            if (tree == null || Volatile.Read(ref _acceptWakeUps) == 0)
            {
                return;
            }

            lock (_pendingWakeUpGate)
            {
                if (Volatile.Read(ref _acceptWakeUps) == 0)
                {
                    return;
                }

                if (_pendingWakeUpSet.Contains(tree))
                {
                    Interlocked.Increment(ref _coalescedWakeUpCount);
                    return;
                }

                if (_pendingWakeUps.Count >= _pendingWakeUpCapacity)
                {
                    Interlocked.Increment(ref _capacityRejectedWakeUpCount);
                    return;
                }

                _pendingWakeUpSet.Add(tree);
                _pendingWakeUps.Enqueue(tree);
                if (_pendingWakeUps.Count > _pendingWakeUpPeak)
                {
                    _pendingWakeUpPeak = _pendingWakeUps.Count;
                }

                Interlocked.Increment(ref _acceptedWakeUpCount);
            }
        }

        private void PromoteWakeUpTrees()
        {
            while (TryDequeueWakeUp(out RuntimeBehaviorTree tree))
            {
                if (tree == null ||
                    tree.IsDisposed ||
                    _lodProvider == null ||
                    !_lodProvider.ContainsTree(tree) ||
                    _manager == null ||
                    _config == null ||
                    !_config.IsValid)
                {
                    continue;
                }

                _manager.UpdatePriority(tree, _config.BoostedPriority);
                tree.TickInterval = _config.BoostedTickInterval;
            }
        }

        private bool TryDequeueWakeUp(out RuntimeBehaviorTree tree)
        {
            lock (_pendingWakeUpGate)
            {
                if (_pendingWakeUps.Count == 0)
                {
                    tree = null;
                    return false;
                }

                tree = _pendingWakeUps.Dequeue();
                _pendingWakeUpSet.Remove(tree);
                return true;
            }
        }

        public BTPriorityTickManagerMemoryStats GetMemoryStats()
        {
            BTPriorityTickManagerCoreMemoryStats core = _manager?.GetMemoryStats() ?? default;
            BTDistanceLODProviderMemoryStats lod = _lodProvider != null
                ? _lodProvider.GetMemoryStats()
                : default;
            int registeredTreeCount = core.TreeCount;
            lock (_pendingWakeUpGate)
            {
                return new BTPriorityTickManagerMemoryStats(
                    registeredTreeCount,
                    _pendingWakeUps.Count,
                    _pendingWakeUpCapacity,
                    _pendingWakeUpPeak,
                    Interlocked.Read(ref _acceptedWakeUpCount),
                    Interlocked.Read(ref _coalescedWakeUpCount),
                    Interlocked.Read(ref _capacityRejectedWakeUpCount),
                    core,
                    lod);
            }
        }

        public int GetPriorityTreeCount(int priority)
        {
            return _manager?.GetTreeCount(priority) ?? 0;
        }

#if UNITY_EDITOR
        private void UpdateDebugStats()
        {
            _totalTreeCount = _manager?.GetTotalCount() ?? 0;
            for (int i = 0; i < 8; i++)
            {
                _priorityTreeCounts[i] = _manager?.GetTreeCount(i) ?? 0;
            }
        }

        private void OnValidate()
        {
            _lodUpdateInterval = Mathf.Max(0.1f, _lodUpdateInterval);
            _pendingWakeUpCapacity = Mathf.Clamp(
                _pendingWakeUpCapacity,
                1,
                MaximumPendingWakeUpCapacity);
            _maximumTreeCount = Mathf.Clamp(
                _maximumTreeCount,
                1,
                BTPriorityTickManager.HardMaximumTreeCount);
        }
#endif

        private static void ValidatePendingWakeUpCapacity(int value)
        {
            if (value < 1 || value > MaximumPendingWakeUpCapacity)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value),
                    value,
                    $"Pending wake-up capacity must be between 1 and {MaximumPendingWakeUpCapacity}.");
            }
        }

        private sealed class RuntimeBehaviorTreeReferenceComparer : IEqualityComparer<RuntimeBehaviorTree>
        {
            public static readonly RuntimeBehaviorTreeReferenceComparer Instance =
                new RuntimeBehaviorTreeReferenceComparer();

            public bool Equals(RuntimeBehaviorTree x, RuntimeBehaviorTree y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(RuntimeBehaviorTree obj)
            {
                return RuntimeHelpers.GetHashCode(obj);
            }
        }
    }
}
