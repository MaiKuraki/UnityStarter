using System;
using CycloneGames.BehaviorTree.Runtime.Data;
using CycloneGames.BehaviorTree.Runtime.Interfaces;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes
{
    public abstract class BTNode : ScriptableObject, IBTNode
    {
        [HideInInspector] public BTState State { get; set; } = BTState.NOT_ENTERED;
        [HideInInspector] public string GUID;
        public Vector2 Position
        {
            get => _position;
            set
            {
                _position = value;
                if (Tree != null)
                {
#if UNITY_EDITOR
                    Tree.OnValidate();
#endif
                }
            }
        }
        public BehaviorTree Tree
        {
            get => _tree;
            set => _tree = value;
        }

        public bool IsStarted
        {
            get => _isStarted;
            set => _isStarted = value;
        }
        /// <summary>
        /// Indicates whether the node can be re-evaluated during execution.
        /// </summary>
        public virtual bool CanReEvaluate => false;
        public virtual bool EnableHijack => false;
        [HideInInspector][SerializeField] private BehaviorTree _tree;
        [HideInInspector][SerializeField] private Vector2 _position;
        private bool _isInitialized = false;
        private bool _isStarted = false;

        string IBTNode.GUID => GUID;

        private void Awake() { }

        /// <summary>
        /// Called when the behavior tree runner is initialized.
        /// </summary>
        public virtual void OnAwake() { }

        /// <summary>
        /// Injects dependencies into the node for DI framework integration.
        /// </summary>
        public virtual void Inject(object container) { }

        /// <summary>
        /// Executes the node and returns its state.
        /// </summary>
        public BTState Run(IBlackBoard blackBoard)
        {
            if (!_isStarted)
            {
                BTStart(blackBoard);
                State = BTState.RUNNING;
            }
            State = OnRun(blackBoard);
            if (State == BTState.FAILURE || State == BTState.SUCCESS)
            {
                BTStop(blackBoard);
            }
            return State;
        }
        private void BTStart(IBlackBoard blackBoard)
        {
            Initialize(blackBoard);
            OnStart(blackBoard);
            _isStarted = true;
        }
        
        /// <summary>
        /// Stops the node execution and calls OnStop.
        /// </summary>
        public void BTStop(IBlackBoard blackBoard)
        {
            if (!_isStarted) return;
            OnStop(blackBoard);
            _isStarted = false;
        }
        
        private void Initialize(IBlackBoard blackBoard)
        {
            if (_isInitialized) return;
            _isInitialized = true;
            OnInitialize(blackBoard);
        }

        /// <summary>
        /// Called when the node starts execution.
        /// </summary>
        protected virtual void OnStart(IBlackBoard blackBoard) { }

        /// <summary>
        /// Called every frame while the node is running. Must return the current node state.
        /// </summary>
        protected virtual BTState OnRun(IBlackBoard blackBoard) { return BTState.SUCCESS; }

        /// <summary>
        /// Called when the node stops execution.
        /// </summary>
        protected virtual void OnStop(IBlackBoard blackBoard) { }
        
        /// <summary>
        /// Called once when the behavior tree is initialized.
        /// </summary>
        protected virtual void OnInitialize(IBlackBoard blackBoard) { }
        /// <summary>
        /// Evaluates the node's condition or logic without executing it.
        /// Used for conditional abort and re-evaluation checks.
        /// </summary>
        /// <param name="blackBoard">The blackboard instance</param>
        /// <returns>The evaluation result state</returns>
        public abstract BTState Evaluate(IBlackBoard blackBoard);
        public void OnValidate()
        {
            try
            {
                CheckIntegrity();
            }
            catch (Exception e)
            {
                Debug.LogError(e);
            }
#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(this);
#endif
        }
        protected virtual void CheckIntegrity() { }
        public virtual BTNode Clone()
        {
            BTNode clone;
            if (Application.isPlaying)
            {
                clone = Instantiate(this);
                clone.name = GetType().Name;
                clone._position = _position;
                clone.GUID = GUID;
            }
            else
            {
                clone = CreateInstance(GetType()) as BTNode;
                clone.name = GetType().Name;
                clone._tree = _tree;
            }
            return clone;
        }

        public virtual void OnDrawGizmos() { }

        /// <summary>
        /// Factory method to create the optimized runtime node execution instance.
        /// </summary>
        /// <returns>A new instance of RuntimeNode</returns>
        public virtual CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode CreateRuntimeNode() 
        { 
            return null; // Base implementation returns null or throws 
        }
    }
}