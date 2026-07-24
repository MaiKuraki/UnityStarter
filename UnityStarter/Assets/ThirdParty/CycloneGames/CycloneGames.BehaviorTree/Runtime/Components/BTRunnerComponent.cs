using System;
using UnityEngine;
using UnityEngine.Serialization;
using Object = UnityEngine.Object;

using CycloneGames.BehaviorTree.Runtime.Attributes;
using CycloneGames.BehaviorTree.Runtime.Core;
using CycloneGames.BehaviorTree.Runtime.Nodes;

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
        private static readonly System.Collections.Generic.List<BTRunnerComponent> _activeRunnersList = new System.Collections.Generic.List<BTRunnerComponent>(64);
        private static readonly System.Collections.Generic.HashSet<BTRunnerComponent> _activeRunnersSet = new System.Collections.Generic.HashSet<BTRunnerComponent>();

        public static System.Collections.Generic.IReadOnlyList<BTRunnerComponent> ActiveRunners => _activeRunnersList;

        public event Action OnTreeStopped;

        [Serializable]
        private class BlackBoardPassObject
        {
            [FormerlySerializedAs("Key")]
            [BehaviorTreeBlackboardKey(RuntimeBlackboardValueType.Object, allowEmpty: true)]
            [SerializeField] private string KeyField;
            [FormerlySerializedAs("Value")]
            [SerializeField] private Object ValueField;

            public string Key
            {
                get => KeyField;
                set => KeyField = value;
            }

            public Object Value => ValueField;
        }

        public BehaviorTree Tree => behaviorTree;
        public RuntimeBehaviorTree RuntimeTree => _runtimeTree;
        public TickMode TickMode => _tickMode;

        public bool IsPaused => _isPaused;
        public bool IsStopped => _isStopped;

        [SerializeField] protected bool _startOnAwake = true;
        [SerializeField] protected TickMode _tickMode = TickMode.Self;
        [SerializeField] protected BehaviorTree behaviorTree;
        [SerializeField] private BlackBoardPassObject[] _initialObjects;

        private bool _isPaused = false;
        private bool _isStopped = false;
        private bool _isRegistered;
        private bool _stopEventRaised;
        private BTTickManagerComponent _registeredTickManager;
        private BTPriorityTickManagerComponent _registeredPriorityManager;
        private BehaviorTree _nextTree;
        private RuntimeBTContext _context;

        private RuntimeBehaviorTree _runtimeTree;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            _activeRunnersList.Clear();
            _activeRunnersSet.Clear();
        }

        private void OnEnable()
        {
            if (_activeRunnersSet.Add(this))
            {
                _activeRunnersList.Add(this);
            }

            RegisterWithManager();
        }

        private void OnDisable()
        {
            UnregisterFromManager();
            if (_activeRunnersSet.Remove(this))
            {
                // Preserve an unordered dense list after the O(n) lookup.
                int idx = _activeRunnersList.IndexOf(this);
                if (idx >= 0)
                {
                    int last = _activeRunnersList.Count - 1;
                    if (idx < last)
                        _activeRunnersList[idx] = _activeRunnersList[last];
                    _activeRunnersList.RemoveAt(last);
                }
            }
        }

        protected virtual void Awake()
        {
            if (behaviorTree == null)
            {
                Debug.LogWarning($"[BTRunnerComponent] Behavior tree is null on {gameObject.name}.");
                return;
            }
            if (!_startOnAwake) return;

            if (!InitializeRuntimeTree())
            {
                _isPaused = true;
                _isStopped = true;
                return;
            }

            RegisterWithManager();
        }

        private void RegisterWithManager()
        {
            if (_isRegistered)
            {
                bool registrationIsAlive =
                    (_tickMode == TickMode.Managed && _registeredTickManager != null) ||
                    (_tickMode == TickMode.PriorityManaged && _registeredPriorityManager != null);
                if (registrationIsAlive)
                {
                    return;
                }

                UnregisterFromManager();
            }

            if (_runtimeTree == null
                || _runtimeTree.IsStopped
                || _isPaused
                || _isStopped
                || !isActiveAndEnabled)
            {
                return;
            }

            if (_tickMode == TickMode.Managed)
            {
                BTTickManagerComponent manager = BTTickManagerComponent.Instance;
                if (manager != null)
                {
                    manager.Register(_runtimeTree);
                    _registeredTickManager = manager;
                    _isRegistered = true;
                }
            }
            else if (_tickMode == TickMode.PriorityManaged)
            {
                BTPriorityTickManagerComponent manager = BTPriorityTickManagerComponent.Instance;
                if (manager != null)
                {
                    manager.Register(_runtimeTree, transform);
                    _registeredPriorityManager = manager;
                    _isRegistered = true;
                }
            }
        }

        private void UnregisterFromManager()
        {
            RuntimeBehaviorTree tree = _runtimeTree;
            if (tree != null)
            {
                if (_registeredTickManager != null)
                {
                    _registeredTickManager.Unregister(tree);
                }

                if (_registeredPriorityManager != null)
                {
                    _registeredPriorityManager.Unregister(tree);
                }
            }

            _registeredTickManager = null;
            _registeredPriorityManager = null;
            _isRegistered = false;
        }

        private bool InitializeRuntimeTree(bool initializeStopped = false)
        {
            if (behaviorTree == null) return false;

            _context ??= new RuntimeBTContext();
            _context.Owner = gameObject;

            // Compile to Pure C# Runtime Tree
            RuntimeBehaviorTree compiledTree = behaviorTree.Compile(_context);
            if (compiledTree == null)
            {
                Debug.LogError($"[BTRunnerComponent] Behavior tree compilation returned null on {gameObject.name}.", this);
                return false;
            }

            try
            {
                if (initializeStopped && !compiledTree.IsStopped)
                {
                    compiledTree.Stop();
                }

                if (initializeStopped)
                {
                    compiledTree.Blackboard.ResetToSchemaDefaults();
                }

                ApplyInitialBlackboardObjects(compiledTree);
                compiledTree.Terminated += HandleTreeTerminated;
                _runtimeTree = compiledTree;
                _stopEventRaised = false;
                return true;
            }
            catch (Exception initializationException)
            {
                _isPaused = true;
                _isStopped = true;
                try
                {
                    compiledTree.Dispose();
                }
                catch (Exception cleanupException)
                {
                    throw new AggregateException(initializationException, cleanupException);
                }

                throw;
            }
        }

        private void ApplyInitialBlackboardObjects(RuntimeBehaviorTree tree = null)
        {
            tree ??= _runtimeTree;
            if (tree == null || tree.Blackboard == null) return;

            if (_initialObjects != null)
            {
                for (int i = 0; i < _initialObjects.Length; i++)
                {
                    var data = _initialObjects[i];
                    if (data == null || string.IsNullOrEmpty(data.Key)) continue;
                    tree.Blackboard.SetObject(data.Key, data.Value);
                }
            }
        }

        private void Update()
        {
            if (_tickMode == TickMode.Managed || _tickMode == TickMode.PriorityManaged)
            {
                RegisterWithManager();
                return;
            }

            if (_isRegistered)
            {
                UnregisterFromManager();
            }

            if (_tickMode != TickMode.Self) return;
            if (_runtimeTree == null) return;
            if (_isPaused || _isStopped) return;

            _runtimeTree.Tick();
        }

        /// <summary>
        /// Manual tick for Manual mode. Returns current tree state.
        /// </summary>
        public RuntimeState ManualTick()
        {
            if (_runtimeTree == null) return RuntimeState.NotEntered;
            if (_isPaused || _isStopped) return _runtimeTree.State;

            return _runtimeTree.Tick();
        }

        private void LateUpdate()
        {
            if (_nextTree == null) return;
            UnregisterFromManager();
            DisposeRuntimeTree();
            behaviorTree = _nextTree;
            bool initialized = InitializeRuntimeTree();
            _isPaused = !initialized;
            _isStopped = !initialized;
            if (initialized)
            {
                RegisterWithManager();
            }
            _nextTree = null;
        }

        private void OnDestroy()
        {
            if (_activeRunnersSet.Remove(this))
            {
                int idx = _activeRunnersList.IndexOf(this);
                if (idx >= 0)
                {
                    int last = _activeRunnersList.Count - 1;
                    if (idx < last)
                        _activeRunnersList[idx] = _activeRunnersList[last];
                    _activeRunnersList.RemoveAt(last);
                }
            }
            UnregisterFromManager();
            DisposeRuntimeTree();
        }

        public void BTSendMessage(string message)
        {
            if (string.IsNullOrEmpty(message)) return;
            if (_runtimeTree == null) return;
            _runtimeTree.Blackboard.SetObject(MESSAGE_KEY, message);
        }

        public void BTSetData(string key, object value)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (_runtimeTree == null) return;

            if (value is int i) _runtimeTree.Blackboard.SetInt(key, i);
            else if (value is float f) _runtimeTree.Blackboard.SetFloat(key, f);
            else if (value is bool b) _runtimeTree.Blackboard.SetBool(key, b);
            else _runtimeTree.Blackboard.SetObject(key, value);
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

        public void SetContext(RuntimeBTContext context)
        {
            EnsureContextCanChange();
            _context = context ?? new RuntimeBTContext();
            _context.Owner = gameObject;

            if (_runtimeTree != null && _runtimeTree.Blackboard != null)
            {
                _runtimeTree.SetContext(_context);
            }
        }

        public void SetServiceResolver(IRuntimeBTServiceResolver resolver)
        {
            EnsureContextCanChange();
            _context ??= new RuntimeBTContext();
            _context.Owner = gameObject;
            _context.ServiceResolver = resolver;

            if (_runtimeTree != null && _runtimeTree.Blackboard != null)
            {
                _runtimeTree.SetContext(_context);
            }
        }

        private void EnsureContextCanChange()
        {
            if (_runtimeTree?.Root != null && _runtimeTree.Root.IsStarted)
            {
                throw new InvalidOperationException(
                    "Behavior tree context cannot change while the tree has an active node stack.");
            }
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
                BTPriorityTickManagerComponent manager = _registeredPriorityManager;
                if (manager == null)
                {
                    manager = BTPriorityTickManagerComponent.Instance;
                }

                manager?.BoostPriority(_runtimeTree, duration);
            }
        }

        public void WakeUp(int boostedTicks = 1)
        {
            if (_runtimeTree == null) return;
            _runtimeTree.WakeUp(boostedTicks);
        }

        public void Stop()
        {
            if (_runtimeTree == null || _runtimeTree.IsStopped)
            {
                return;
            }

            _runtimeTree.Stop();
        }

        public void Play()
        {
            bool preparedForPlay = false;
            if (_runtimeTree == null)
            {
                if (!InitializeRuntimeTree(initializeStopped: true))
                {
                    _isPaused = true;
                    _isStopped = true;
                    return;
                }

                preparedForPlay = true;
            }

            if (!preparedForPlay)
            {
                if (!_runtimeTree.IsStopped)
                {
                    _runtimeTree.Stop();
                }

                _runtimeTree.Blackboard.ResetToSchemaDefaults();
                ApplyInitialBlackboardObjects();
            }

            _stopEventRaised = false;
            _runtimeTree.Play();
            _isPaused = false;
            _isStopped = false;
            RegisterWithManager();
        }

        public void Pause()
        {
            if (_isPaused || _isStopped) return;
            _isPaused = true;
            UnregisterFromManager();
        }

        public void Resume()
        {
            if (_runtimeTree == null || _isStopped)
            {
                Play();
                return;
            }
            _isPaused = false;
            RegisterWithManager();
        }

        private void HandleTreeTerminated(RuntimeState state)
        {
            _isPaused = true;
            _isStopped = true;
            UnregisterFromManager();

            if (_stopEventRaised)
            {
                return;
            }

            _stopEventRaised = true;
            OnTreeStopped?.Invoke();
        }

        private void DisposeRuntimeTree()
        {
            if (_runtimeTree == null)
            {
                return;
            }

            RuntimeBehaviorTree tree = _runtimeTree;
            _runtimeTree = null;
            tree.Terminated -= HandleTreeTerminated;
            tree.Dispose();
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
