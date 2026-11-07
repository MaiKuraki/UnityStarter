using CycloneGames.Logging;
using UnityEngine;
using CycloneGames.BehaviorTree.Runtime.Core;

namespace CycloneGames.BehaviorTree.Runtime.Components
{
    [DisallowMultipleComponent]
    /// <summary>
    /// Scene-authored scheduler for managed trees. Instance only resolves an existing
    /// component; CreateInstance is the explicit composition-root entry point.
    /// </summary>
    public class BTTickManagerComponent : MonoBehaviour
    {
        private static readonly LogChannel Log = BehaviorTreeRuntimeLog.Channel;

        private static BTTickManagerComponent _instance;
        private static bool _isQuitting;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            _instance = null;
            _isQuitting = false;
        }

        public static bool HasInstance => _instance != null;

        /// <summary>
        /// Resolves the authoritative scene instance without creating anything.
        /// Returns null when no manager component has been placed in the scene.
        /// </summary>
        public static BTTickManagerComponent Instance
        {
            get
            {
                if (_isQuitting) return null;

                if (_instance == null)
                {
                    _instance = BTManagerInstanceRegistry.FindExisting<BTTickManagerComponent>(nameof(BTTickManagerComponent));
                }
                return _instance;
            }
        }

        /// <summary>
        /// Explicitly creates a persistent manager; scene-authored managers are discovered through
        /// Instance without allocation.
        /// </summary>
        public static BTTickManagerComponent CreateInstance()
        {
            if (_isQuitting) return null;

            BTTickManagerComponent existing = Instance;
            if (existing != null) return existing;

            var go = new GameObject("[BTTickManager]");
            BTTickManagerComponent created = go.AddComponent<BTTickManagerComponent>();
            DontDestroyOnLoad(go);
            return created;
        }

        [Header("Capacity")]
        [SerializeField, Min(1)] private int _maximumTreeCount = BTCapacityLimits.DefaultAgentCount;

        private Core.BTTickManager _manager;
        private bool _registrationCapacityReported;

        public int TickBudget
        {
            get => GetOrCreateManager().TickBudget;
            set => GetOrCreateManager().TickBudget = value;
        }

        public int TreeCount => GetOrCreateManager().Count;

        private void OnEnable()
        {
            BTManagerInstanceRegistry.Register(this);
        }

        private void OnDisable()
        {
            BTManagerInstanceRegistry.Unregister(this);
        }

        private void Awake()
        {
            if (_instance == null)
            {
                _instance = BTManagerInstanceRegistry.FindExisting<BTTickManagerComponent>(nameof(BTTickManagerComponent));
            }

            if (_instance != null && _instance != this)
            {
                Log.Warning(
                    $"[BTTickManagerComponent] Removing duplicate manager component from '{gameObject.name}'. " +
                    $"The active instance is '{_instance.gameObject.name}'.");
                Destroy(this);
                return;
            }
            _instance = this;
            GetOrCreateManager();
        }

        private void OnApplicationQuit()
        {
            _isQuitting = true;
        }

        private void Update()
        {
            _manager?.Tick();
        }

        private void OnDestroy()
        {
            _manager?.Clear();
            if (_instance == this) _instance = null;
        }

        public void Register(Core.RuntimeBehaviorTree tree)
        {
            Core.BTTickManager manager = GetOrCreateManager();
            Core.BTTickManagerMemoryStats before = manager.GetMemoryStats();
            if (manager.TryRegister(tree) || _registrationCapacityReported)
            {
                return;
            }

            Core.BTTickManagerMemoryStats after = manager.GetMemoryStats();
            if (after.CapacityRejectedTreeCount <= before.CapacityRejectedTreeCount &&
                after.CapacityRejectedMutationCount <= before.CapacityRejectedMutationCount)
            {
                return;
            }

            _registrationCapacityReported = true;
            Log.Error(
                $"[BTTickManagerComponent] Register was rejected because managed tree or " +
                $"deferred-mutation capacity was exhausted on '{gameObject.name}'. " +
                "Use TryRegister to handle admission failure.");
        }

        public bool TryRegister(Core.RuntimeBehaviorTree tree) => GetOrCreateManager().TryRegister(tree);
        public void Unregister(Core.RuntimeBehaviorTree tree) => _manager?.Unregister(tree);

        public Core.BTTickManagerMemoryStats GetMemoryStats()
        {
            return GetOrCreateManager().GetMemoryStats();
        }

        private Core.BTTickManager GetOrCreateManager()
        {
            if (_manager == null)
            {
                int maximumTreeCount = Mathf.Clamp(
                    _maximumTreeCount,
                    1,
                    BTCapacityLimits.MaxAgentCount);
                int initialCapacity = Mathf.Min(
                    BTCapacityLimits.DefaultTickManagerCapacity,
                    maximumTreeCount);
                _manager = new Core.BTTickManager(
                    initialCapacity,
                    maximumTreeCount,
                    maximumTreeCount);
            }

            return _manager;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            _maximumTreeCount = Mathf.Clamp(
                _maximumTreeCount,
                1,
                BTCapacityLimits.MaxAgentCount);
        }
#endif
    }
}
