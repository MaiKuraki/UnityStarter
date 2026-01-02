using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Components
{
    public class BTTickManagerComponent : MonoBehaviour
    {
        private static BTTickManagerComponent _instance;
        private static bool _isQuitting;
        
        public static bool HasInstance => _instance != null;

        public static BTTickManagerComponent Instance
        {
            get
            {
                if (_isQuitting) return null;
                
                if (_instance == null)
                {
                    var go = new GameObject("[BTTickManager]");
                    _instance = go.AddComponent<BTTickManagerComponent>();
                    DontDestroyOnLoad(go);
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
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
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
