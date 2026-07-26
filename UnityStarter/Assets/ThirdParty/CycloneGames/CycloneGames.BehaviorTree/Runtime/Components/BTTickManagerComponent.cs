using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Components
{
    [DisallowMultipleComponent]
    public class BTTickManagerComponent : MonoBehaviour
    {
        private static BTTickManagerComponent _instance;
        private static bool _isQuitting;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            _instance = null;
            _isQuitting = false;
        }

        public static bool HasInstance => _instance != null;

        public static BTTickManagerComponent Instance
        {
            get
            {
                if (_isQuitting) return null;

                if (_instance == null)
                {
                    _instance = BTManagerSceneResolver.FindExisting<BTTickManagerComponent>(nameof(BTTickManagerComponent));
                    if (_instance == null)
                    {
                        var go = new GameObject("[BTTickManager]");
                        BTTickManagerComponent created = go.AddComponent<BTTickManagerComponent>();
                        if (_instance == null)
                        {
                            _instance = created;
                        }
                        DontDestroyOnLoad(go);
                    }
                }
                return _instance;
            }
        }

        [Header("Capacity")]
        [SerializeField, Min(1)] private int _maximumTreeCount = Core.BTTickManager.DefaultMaximumTreeCount;

        private Core.BTTickManager _manager;
        private bool _legacyRegistrationCapacityReported;

        public int TickBudget
        {
            get => GetOrCreateManager().TickBudget;
            set => GetOrCreateManager().TickBudget = value;
        }

        public int TreeCount => GetOrCreateManager().Count;

        private void Awake()
        {
            if (_instance == null)
            {
                _instance = BTManagerSceneResolver.FindExisting<BTTickManagerComponent>(nameof(BTTickManagerComponent));
            }

            if (_instance != null && _instance != this)
            {
                Debug.LogWarning(
                    $"[BTTickManagerComponent] Removing duplicate manager component from '{gameObject.name}'. " +
                    $"The active instance is '{_instance.gameObject.name}'.",
                    this);
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
            if (manager.TryRegister(tree) || _legacyRegistrationCapacityReported)
            {
                return;
            }

            Core.BTTickManagerMemoryStats after = manager.GetMemoryStats();
            if (after.CapacityRejectedTreeCount <= before.CapacityRejectedTreeCount &&
                after.CapacityRejectedMutationCount <= before.CapacityRejectedMutationCount)
            {
                return;
            }

            _legacyRegistrationCapacityReported = true;
            Debug.LogError(
                $"[BTTickManagerComponent] Legacy Register was rejected because managed tree or " +
                $"deferred-mutation capacity was exhausted on '{gameObject.name}'. " +
                "Use TryRegister to handle admission failure.",
                this);
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
                    Core.BTTickManager.HardMaximumTreeCount);
                int initialCapacity = Mathf.Min(
                    Core.BTTickManager.DefaultInitialCapacity,
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
                Core.BTTickManager.HardMaximumTreeCount);
        }
#endif
    }
}
