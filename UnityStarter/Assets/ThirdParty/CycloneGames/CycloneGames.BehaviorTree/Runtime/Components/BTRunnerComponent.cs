using System;
using CycloneGames.BehaviorTree.Runtime.Nodes;
using UnityEngine;
using Object = UnityEngine.Object;
using CycloneGames.BehaviorTree.Runtime.Core;

namespace CycloneGames.BehaviorTree.Runtime.Components
{
    public enum TickMode
    {
        Self,            // Component's Update() handles tick
        Managed,         // BTTickManager handles tick (simple)
        PriorityManaged, // BTPriorityTickManager with LOD (for 1000+ AIs)
        Manual           // User calls ManualTick() directly (full control)
    }

    public class BTRunnerComponent : MonoBehaviour
    {
        private const string MESSAGE_KEY = "Message";

        public event Action OnTreeStopped;

        [Serializable]
        private class BlackBoardPassObject
        {
            public string Key;
            public Object Value;
        }

        public BehaviorTree Tree => behaviorTree;
        public BlackBoard BlackBoard => _blackBoard;
        public RuntimeBehaviorTree RuntimeTree => _runtimeTree;
        public TickMode TickMode => _tickMode;

        public bool IsPaused => _isPaused;
        public bool IsStopped => _isStopped;

        [SerializeField] protected bool _startOnAwake = true;
        [SerializeField] protected TickMode _tickMode = TickMode.Self;
        [SerializeField] protected BehaviorTree behaviorTree;
        [SerializeField] private BlackBoardPassObject[] _initialObjects;
        [HideInInspector][SerializeField] BlackBoard _blackBoard = new BlackBoard();

        private bool _isPaused = false;
        private bool _isStopped = false;
        private BehaviorTree _nextTree;

        private RuntimeBehaviorTree _runtimeTree;

        private void Awake()
        {
            if (behaviorTree == null)
            {
                Debug.LogWarning($"[BTRunnerComponent] Behavior tree is null on {gameObject.name}.");
                return;
            }
            if (!_startOnAwake) return;

            InitializeRuntimeTree();
            RegisterWithManager();
        }

        private void RegisterWithManager()
        {
            if (_runtimeTree == null) return;

            if (_tickMode == TickMode.Managed)
            {
                BTTickManagerComponent.Instance.Register(_runtimeTree);
            }
            else if (_tickMode == TickMode.PriorityManaged)
            {
                BTPriorityTickManagerComponent.Instance.Register(_runtimeTree, transform);
            }
        }

        private void UnregisterFromManager()
        {
            if (_runtimeTree == null) return;

            if (_tickMode == TickMode.Managed && BTTickManagerComponent.HasInstance)
            {
                BTTickManagerComponent.Instance?.Unregister(_runtimeTree);
            }
            else if (_tickMode == TickMode.PriorityManaged && BTPriorityTickManagerComponent.HasInstance)
            {
                BTPriorityTickManagerComponent.Instance?.Unregister(_runtimeTree);
            }
        }

        private void InitializeRuntimeTree()
        {
            if (behaviorTree == null) return;

            // Compile to Pure C# Runtime Tree
            _runtimeTree = behaviorTree.Compile(gameObject);

            // Initialize Blackboard Data
            if (_runtimeTree != null && _runtimeTree.Blackboard != null)
            {
                // Transfer initial objects
                if (_initialObjects != null)
                {
                    for (int i = 0; i < _initialObjects.Length; i++)
                    {
                        var data = _initialObjects[i];
                        if (data == null || string.IsNullOrEmpty(data.Key)) continue;
                        _runtimeTree.Blackboard.SetObject(Animator.StringToHash(data.Key), data.Value);
                    }
                }

                // Transfer serialized blackboard data (if any standard way exists, or just rely on runtime set)
                // Note: The original BlackBoard class might have data. We would need to copy it if it was populated.
                // Assuming _blackBoard is mostly for serialization and runtime storage in the old system.
            }
        }

        private void Update()
        {
            if (_tickMode != TickMode.Self) return;
            if (_runtimeTree == null) return;
            if (_isPaused) return;

            var lastState = _runtimeTree.Tick();
            if (lastState == RuntimeState.Failure || lastState == RuntimeState.Success)
            {
                Stop();
            }
        }

        /// <summary>
        /// Manual tick for Manual mode. Returns current tree state.
        /// </summary>
        public RuntimeState ManualTick()
        {
            if (_runtimeTree == null) return RuntimeState.NotEntered;
            if (_isPaused) return _runtimeTree.State;

            var state = _runtimeTree.Tick();
            if (state == RuntimeState.Failure || state == RuntimeState.Success)
            {
                _isStopped = true;
            }
            return state;
        }

        private void LateUpdate()
        {
            if (_nextTree == null) return;
            if (_runtimeTree != null)
            {
                _runtimeTree.Stop();
            }
            behaviorTree = _nextTree;
            InitializeRuntimeTree();
            _nextTree = null;
        }

        private void OnDestroy()
        {
            UnregisterFromManager();
            if (_runtimeTree != null)
            {
                _runtimeTree.Stop();
            }
        }

        public void BTSendMessage(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            if (_runtimeTree == null) return;
            _runtimeTree.Blackboard.SetObject(Animator.StringToHash(MESSAGE_KEY), message);
        }

        public void BTSetData(string key, object value)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (_runtimeTree == null) return;

            int hash = Animator.StringToHash(key);
            if (value is int i) _runtimeTree.Blackboard.SetInt(hash, i);
            else if (value is float f) _runtimeTree.Blackboard.SetFloat(hash, f);
            else if (value is bool b) _runtimeTree.Blackboard.SetBool(hash, b);
            else _runtimeTree.Blackboard.SetObject(hash, value);
        }

        public void BTRemoveData(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (_runtimeTree == null) return;
            _runtimeTree.Blackboard.Remove(key);
        }

        public void SetTree(BehaviorTree newTree)
        {
            if (newTree == null)
            {
                Debug.LogWarning($"[BTRunnerComponent] Cannot set null behavior tree on {gameObject.name}.");
                return;
            }
            _nextTree = newTree;
        }

        public void SetTickMode(TickMode mode)
        {
            if (_tickMode == mode) return;
            UnregisterFromManager();
            _tickMode = mode;
            RegisterWithManager();
        }

        public void SetTickInterval(int interval)
        {
            if (_runtimeTree == null) return;
            _runtimeTree.TickInterval = interval;
        }

        public void BoostPriority(float duration)
        {
            if (_runtimeTree == null) return;
            if (_tickMode == TickMode.PriorityManaged)
            {
                BTPriorityTickManagerComponent.Instance.BoostPriority(_runtimeTree, duration);
            }
        }

        public void Stop()
        {
            if (_runtimeTree != null)
            {
                _runtimeTree.Stop();
            }
            _isPaused = true;
            _isStopped = true;
            OnTreeStopped?.Invoke();
        }

        public void Play()
        {
            if (_runtimeTree == null)
            {
                InitializeRuntimeTree();
            }

            if (_runtimeTree == null) return;

            if (!_isStopped)
            {
                _runtimeTree.Stop();
            }
            _isPaused = false;
            _isStopped = false;
        }

        public void Pause()
        {
            _isPaused = true;
        }

        public void Resume()
        {
            if (_runtimeTree == null)
            {
                InitializeRuntimeTree();
            }
            _isPaused = false;
        }

        protected virtual void OnValidate()
        {
            if (_initialObjects == null) return;

            for (int i = 0; i < _initialObjects.Length; i++)
            {
                var objectSet = _initialObjects[i];
                if (objectSet == null) continue;

                if (objectSet.Value == null)
                {
                    if (!string.IsNullOrEmpty(objectSet.Key))
                    {
                        Debug.LogWarning($"[BTRunnerComponent] Object value is null for key: {objectSet.Key}");
                    }
                    continue;
                }

                if (string.IsNullOrEmpty(objectSet.Key))
                {
                    objectSet.Key = objectSet.Value.name;
                }
            }
        }

        private void OnDrawGizmos()
        {
            if (behaviorTree == null) return;
            behaviorTree.OnDrawGizmos();
        }
    }
}