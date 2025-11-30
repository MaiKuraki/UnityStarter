using System;
using CycloneGames.BehaviorTree.Runtime.Data;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes
{
    public abstract class BTNode : ScriptableObject
    {
        [HideInInspector] public BTState State = BTState.NOT_ENTERED;
        [HideInInspector] public string GUID;
        public Vector2 Position
        {
            get => _position;
            set
            {
                _position = value;
                Tree.OnValidate();
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
        /// This property is used to check if the node can be re-evaluated.
        /// </summary>
        public virtual bool CanReEvaluate => false;
        public virtual bool EnableHijack => false;
        [HideInInspector][SerializeField] private BehaviorTree _tree;
        [HideInInspector][SerializeField] private Vector2 _position;
        private bool _isInitialized = false;
        private bool _isStarted = false;

        private void Awake() { }

        /// <summary>
        /// This method is called when the behavior tree runner is awake.
        /// </summary>
        public virtual void OnAwake() { }
        /// <summary>
        /// This method is called when the node is run.
        /// </summary>
        /// <param name="blackBoard"> The blackboard of the behavior tree. </param>
        /// <returns> The state of the node. </returns>
        public BTState Run(BlackBoard blackBoard)
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
        /// <summary>
        /// This method is called when the node is started.
        /// </summary>
        private void BTStart(BlackBoard blackBoard)
        {
            Initialize(blackBoard);
            OnStart(blackBoard);
            _isStarted = true;
        }
        /// <summary>
        /// This method is called when the node is stopped.
        /// </summary>
        public void BTStop(BlackBoard blackBoard)
        {
            if (!_isStarted) return;
            OnStop(blackBoard);
            _isStarted = false;
        }
        /// <summary>
        /// This method is called once when the behavior tree is initialized.
        /// </summary>
        private void Initialize(BlackBoard blackBoard)
        {
            if (_isInitialized) return;
            _isInitialized = true;
            OnInitialize(blackBoard);
        }

        /// <summary>
        /// This method is called when the node is started.
        /// </summary>
        protected virtual void OnStart(BlackBoard blackBoard) { }

        /// <summary>
        /// This method is called every frame while the node is running.
        /// And it should return the state of the node.
        /// </summary>
        /// <param name="blackBoard"> The blackboard of the behavior tree. </param>
        /// <returns> The state of the node. </returns>
        protected virtual BTState OnRun(BlackBoard blackBoard) { return BTState.SUCCESS; }

        /// <summary>
        /// This method is called when the node is stopped.
        /// </summary>
        protected virtual void OnStop(BlackBoard blackBoard) { }
        /// <summary>
        /// This method is called when the behavior tree is initialized.
        /// </summary>
        protected virtual void OnInitialize(BlackBoard blackBoard) { }
        /// <summary>
        /// This method is called when the node is evaluated.
        /// </summary>
        /// <param name="blackBoard"> The blackboard of the behavior tree. </param>
        /// <returns> The state of the node. </returns>
        public abstract BTState Evaluate(BlackBoard blackBoard);
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
    }
}