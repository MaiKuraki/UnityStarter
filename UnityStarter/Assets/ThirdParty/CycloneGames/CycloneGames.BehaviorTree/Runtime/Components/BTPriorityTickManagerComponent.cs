using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;
using CycloneGames.BehaviorTree.Runtime.Core;

namespace CycloneGames.BehaviorTree.Runtime.Components
{
    [DisallowMultipleComponent]
    public class BTPriorityTickManagerComponent : MonoBehaviour
    {
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

        [Header("Reference Point")]
        [SerializeField] private Transform _referencePoint;
        [SerializeField] private string _playerTag = "Player";
        [SerializeField] private bool _autoFindPlayer = true;

        private BTPriorityTickManager _manager;
        private BTDistanceLODProvider _lodProvider;
        private readonly ConcurrentQueue<RuntimeBehaviorTree> _pendingWakeUps = new ConcurrentQueue<RuntimeBehaviorTree>();
        private double _lastLODUpdateTime;
        private bool _initialized;
        private int _acceptWakeUps;
        private string _lastConfigError;

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
                Debug.LogWarning(
                    $"[BTPriorityTickManagerComponent] Removing duplicate manager component from '{gameObject.name}'. " +
                    $"The active instance is '{_instance.gameObject.name}'.",
                    this);
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

            int[] budgets = _config != null && _config.TryValidate(out _)
                ? _config.PriorityBudgets
                : null;
            _manager = new BTPriorityTickManager(budgets);

            if (!TryGetComponent(out _lodProvider))
            {
                _lodProvider = gameObject.AddComponent<BTDistanceLODProvider>();
            }
            _lodProvider.EnsureInitialized();
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
                    Debug.LogError($"[BTPriorityTickManager] Invalid BTLODConfig '{_config.name}': {configError}", _config);
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
                Debug.LogError(
                    $"[BTPriorityTickManager] Player tag '{_playerTag}' is not defined. Automatic lookup was disabled. {exception.Message}",
                    this);
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

            while (_pendingWakeUps.TryDequeue(out _))
            {
            }

            _manager?.Clear();
            if (_instance == this) _instance = null;
        }

        public void Register(RuntimeBehaviorTree tree, Transform treeTransform)
        {
            if (tree == null) return;
            if (!_initialized) Initialize();
            if (_lodProvider.ContainsTree(tree)) return;

            _lodProvider.RegisterTree(tree, treeTransform);
            tree.WakeUpRequested += EnqueueWakeUp;
            _lodProvider.UpdateLOD(tree);

            int priority = _lodProvider.GetPriority(tree);
            tree.TickInterval = _lodProvider.GetTickInterval(tree);

            _manager.Register(tree, priority);
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
            if (tree != null && Volatile.Read(ref _acceptWakeUps) != 0)
            {
                _pendingWakeUps.Enqueue(tree);
            }
        }

        private void PromoteWakeUpTrees()
        {
            while (_pendingWakeUps.TryDequeue(out RuntimeBehaviorTree tree))
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
        }
#endif
    }
}
