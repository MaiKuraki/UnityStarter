using UnityEngine;
using CycloneGames.BehaviorTree.Runtime.Core;

namespace CycloneGames.BehaviorTree.Runtime.Components
{
    public class BTPriorityTickManagerComponent : MonoBehaviour
    {
        private static BTPriorityTickManagerComponent _instance;
        public static BTPriorityTickManagerComponent Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("[BTPriorityTickManager]");
                    _instance = go.AddComponent<BTPriorityTickManagerComponent>();
                    DontDestroyOnLoad(go);
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
        private float _lastLODUpdateTime;
        private bool _initialized;

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
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            Initialize();
        }

        private void Initialize()
        {
            if (_initialized) return;

            _manager = new BTPriorityTickManager(
                _config != null ? _config.PriorityBudgets : null
            );

            _lodProvider = gameObject.AddComponent<BTDistanceLODProvider>();
            ApplyConfig();

            if (_autoFindPlayer && _referencePoint == null)
            {
                TryFindPlayer();
            }

            _initialized = true;
        }

        private void ApplyConfig()
        {
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

            var player = GameObject.FindGameObjectWithTag(_playerTag);
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

            float currentTime = Time.time;
            if (currentTime - _lastLODUpdateTime >= _lodUpdateInterval)
            {
                if (_autoFindPlayer && _referencePoint == null)
                {
                    TryFindPlayer();
                }

                if (_lodProvider != null)
                {
                    _lodProvider.UpdateAllLOD();
                }
                UpdateAllPriorities();
                _lastLODUpdateTime = currentTime;

#if UNITY_EDITOR
                UpdateDebugStats();
#endif
            }

            _manager?.Tick();
        }

        private void OnDestroy()
        {
            _manager?.Clear();
            if (_instance == this) _instance = null;
        }

        public void Register(RuntimeBehaviorTree tree, Transform treeTransform)
        {
            if (tree == null) return;
            if (!_initialized) Initialize();

            _lodProvider.RegisterTree(tree, treeTransform);
            _lodProvider.UpdateLOD(tree);

            int priority = _lodProvider.GetPriority(tree);
            tree.TickInterval = _lodProvider.GetTickInterval(tree);

            _manager.Register(tree, priority);
        }

        public void Unregister(RuntimeBehaviorTree tree)
        {
            if (tree == null) return;
            _lodProvider?.UnregisterTree(tree);
            _manager?.Unregister(tree);
        }

        public void BoostPriority(RuntimeBehaviorTree tree, float duration)
        {
            if (tree == null || _lodProvider == null) return;

            _lodProvider.BoostPriority(tree, duration);

            if (_config != null && _manager != null)
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
            // Redistribute trees based on updated LOD values
            // This is called periodically to handle moving AIs
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
