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

        private readonly Core.BTTickManager _manager = new Core.BTTickManager();

        public int TickBudget
        {
            get => _manager.TickBudget;
            set => _manager.TickBudget = value;
        }

        public int TreeCount => _manager.Count;

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
        }

        private void OnApplicationQuit()
        {
            _isQuitting = true;
        }

        private void Update()
        {
            _manager.Tick();
        }

        private void OnDestroy()
        {
            _manager.Clear();
            if (_instance == this) _instance = null;
        }

        public void Register(Core.RuntimeBehaviorTree tree) => _manager.Register(tree);
        public void Unregister(Core.RuntimeBehaviorTree tree) => _manager.Unregister(tree);
    }
}
