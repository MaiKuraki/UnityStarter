using System.Collections.Generic;

namespace CycloneGames.BehaviorTree.Runtime.Core
{
    public enum RuntimeState
    {
        NotEntered,
        Running,
        Success,
        Failure
    }

    /// <summary>
    /// Base class for all runtime behavior tree nodes. 
    /// Designed to be GC-free during execution and thread-safe (when instantiated per tree).
    /// </summary>
    public abstract class RuntimeNode
    {
        public RuntimeState State { get; protected set; } = RuntimeState.NotEntered;
        public bool IsStarted { get; private set; } = false;
        public string GUID { get; set; }

        public virtual void OnAwake() { }

        public RuntimeState Run(RuntimeBlackboard blackboard)
        {
            if (!IsStarted)
            {
                OnStart(blackboard);
                IsStarted = true;
                State = RuntimeState.Running;
            }

            State = OnRun(blackboard);

            if (State == RuntimeState.Success || State == RuntimeState.Failure)
            {
                OnStop(blackboard);
                IsStarted = false;
            }

            return State;
        }

        public void Abort(RuntimeBlackboard blackboard)
        {
            if (IsStarted)
            {
                OnStop(blackboard);
                IsStarted = false;
                State = RuntimeState.NotEntered;
            }
        }

        protected virtual void OnStart(RuntimeBlackboard blackboard) { }
        protected abstract RuntimeState OnRun(RuntimeBlackboard blackboard);
        protected virtual void OnStop(RuntimeBlackboard blackboard) { }
    }
}
