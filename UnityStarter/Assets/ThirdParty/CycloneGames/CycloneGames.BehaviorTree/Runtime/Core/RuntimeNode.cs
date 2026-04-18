using System;

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
    /// Pre/Post condition failure policy (mirrors BehaviorTree.CPP v4 scripting conditions).
    /// Attached to RuntimeNode via SetPreCondition / SetPostCondition.
    /// </summary>
    public enum ConditionPolicy : byte
    {
        /// <summary>If condition returns false, node returns Failure without executing (BT.CPP _skipIf).</summary>
        SkipWhenFalse,
        /// <summary>If condition returns false, node returns Success without executing (BT.CPP _successIf inverted).</summary>
        SucceedWhenFalse,
        /// <summary>If condition returns false while running, abort and return Failure (BT.CPP _while).</summary>
        AbortWhenFalse,
    }

    /// <summary>
    /// Compact condition entry. Stored in a fixed-size array (0GC iteration).
    /// Condition func is allocated once during setup — zero per-tick allocation.
    /// </summary>
    public struct NodeCondition
    {
        public Func<RuntimeBlackboard, bool> Check;
        public ConditionPolicy Policy;
    }

    public abstract class RuntimeNode
    {
        public RuntimeState State { get; protected set; } = RuntimeState.NotEntered;
        public bool IsStarted { get; private set; } = false;
        public string GUID { get; set; }

        // Event-driven execution: nodes can signal the tree to wake up
        internal RuntimeBehaviorTree OwnerTree { get; set; }

        // Pre/Post conditions (BehaviorTree.CPP v4 pattern)
        // Allocated once during setup; null when unused (zero cost).
        private NodeCondition[] _preConditions;
        private int _preConditionCount;
        private NodeCondition[] _postConditions;
        private int _postConditionCount;

        public virtual bool CanEvaluate => false;

        public virtual bool Evaluate(RuntimeBlackboard blackboard) => true;

        public virtual void OnAwake() { }

        /// <summary>
        /// Attach a pre-condition evaluated every tick before OnRun.
        /// Multiple conditions can be stacked (evaluated in order, first failure applies).
        /// </summary>
        public void AddPreCondition(Func<RuntimeBlackboard, bool> check, ConditionPolicy policy = ConditionPolicy.SkipWhenFalse)
        {
            if (check == null) return;
            if (_preConditions == null) _preConditions = new NodeCondition[2];
            if (_preConditionCount >= _preConditions.Length)
                Array.Resize(ref _preConditions, _preConditions.Length * 2);
            _preConditions[_preConditionCount++] = new NodeCondition { Check = check, Policy = policy };
        }

        /// <summary>
        /// Attach a post-condition evaluated after OnRun returns Success or Failure.
        /// Can override the result (e.g., force success on certain blackboard state).
        /// </summary>
        public void AddPostCondition(Func<RuntimeBlackboard, bool> check, ConditionPolicy policy = ConditionPolicy.SkipWhenFalse)
        {
            if (check == null) return;
            if (_postConditions == null) _postConditions = new NodeCondition[2];
            if (_postConditionCount >= _postConditions.Length)
                Array.Resize(ref _postConditions, _postConditions.Length * 2);
            _postConditions[_postConditionCount++] = new NodeCondition { Check = check, Policy = policy };
        }

        public RuntimeState Run(RuntimeBlackboard blackboard)
        {
            // Pre-condition evaluation (before execution)
            if (_preConditionCount > 0)
            {
                var preResult = EvaluateConditions(_preConditions, _preConditionCount, blackboard);
                if (preResult != RuntimeState.NotEntered)
                {
                    if (IsStarted) Abort(blackboard);
                    return preResult;
                }
            }

            if (!IsStarted)
            {
                OnStart(blackboard);
                IsStarted = true;
                State = RuntimeState.Running;
            }

            var previousState = State;
            State = OnRun(blackboard);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (State != previousState && OwnerTree?.StatusLogger != null)
            {
                OwnerTree.StatusLogger.Log(GUID, previousState, State, RuntimeBTTime.GetUnityTime(false));
            }
#endif

            // Post-condition evaluation (override completed result)
            if (_postConditionCount > 0 && State != RuntimeState.Running)
            {
                var postResult = EvaluateConditions(_postConditions, _postConditionCount, blackboard);
                if (postResult != RuntimeState.NotEntered)
                    State = postResult;
            }

            if (State == RuntimeState.Success || State == RuntimeState.Failure)
            {
                OnStop(blackboard);
                IsStarted = false;
            }

            return State;
        }

        /// <summary>
        /// Evaluate condition array. Returns NotEntered if all pass, otherwise the forced state.
        /// </summary>
        private RuntimeState EvaluateConditions(NodeCondition[] conditions, int count, RuntimeBlackboard bb)
        {
            for (int i = 0; i < count; i++)
            {
                ref var c = ref conditions[i];
                bool passed = c.Check(bb);
                if (!passed)
                {
                    switch (c.Policy)
                    {
                        case ConditionPolicy.SkipWhenFalse:
                        case ConditionPolicy.AbortWhenFalse:
                            return RuntimeState.Failure;
                        case ConditionPolicy.SucceedWhenFalse:
                            return RuntimeState.Success;
                    }
                }
            }
            return RuntimeState.NotEntered; // all passed
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

        /// <summary>
        /// Resets a completed node's state to NotEntered.
        /// Safe to call on nodes that have finished (IsStarted == false).
        /// Used by composite nodes when restarting iterations (e.g. Repeat).
        /// </summary>
        public void ResetState()
        {
            if (!IsStarted)
            {
                State = RuntimeState.NotEntered;
            }
        }

        /// <summary>
        /// Signals the owning tree to schedule a tick (for event-driven execution).
        /// Call from async callbacks, coroutines, or external systems.
        /// </summary>
        protected void EmitWakeUpSignal()
        {
            OwnerTree?.RequestWakeUp(2);
        }

        protected virtual void OnStart(RuntimeBlackboard blackboard) { }
        protected abstract RuntimeState OnRun(RuntimeBlackboard blackboard);
        protected virtual void OnStop(RuntimeBlackboard blackboard) { }
    }

    /// <summary>
    /// Recommended pattern for async/polling actions. Provides a clear lifecycle:
    /// OnActionStart  → called once when entering from IDLE
    /// OnActionRunning → called each tick while RUNNING (poll for completion)
    /// OnActionHalted  → called when aborted by parent or tree shutdown
    /// Mirrors BehaviorTree.CPP's StatefulActionNode pattern.
    /// </summary>
    public abstract class RuntimeStatefulActionNode : RuntimeNode
    {
        protected abstract RuntimeState OnActionStart(RuntimeBlackboard blackboard);
        protected abstract RuntimeState OnActionRunning(RuntimeBlackboard blackboard);
        protected virtual void OnActionHalted(RuntimeBlackboard blackboard) { }

        private bool _wasRunning;
        private bool _completedNormally;

        protected sealed override void OnStart(RuntimeBlackboard blackboard)
        {
            _wasRunning = false;
            _completedNormally = false;
        }

        protected sealed override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            if (!_wasRunning)
            {
                var result = OnActionStart(blackboard);
                if (result == RuntimeState.Running)
                {
                    _wasRunning = true;
                }
                else
                {
                    _completedNormally = true;
                }
                return result;
            }

            var state = OnActionRunning(blackboard);
            if (state != RuntimeState.Running)
            {
                _completedNormally = true;
            }
            return state;
        }

        // Abort() sets State = NotEntered before calling OnStop,
        // so we track completion separately to detect halts.
        protected sealed override void OnStop(RuntimeBlackboard blackboard)
        {
            if (_wasRunning && !_completedNormally)
            {
                OnActionHalted(blackboard);
            }
            _wasRunning = false;
            _completedNormally = false;
        }
    }
}
