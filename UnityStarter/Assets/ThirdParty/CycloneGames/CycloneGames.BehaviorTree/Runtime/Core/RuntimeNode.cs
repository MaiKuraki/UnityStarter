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

    public enum RuntimeNodeExitReason : byte
    {
        Completed,
        Aborted,
        Faulted
    }

    /// <summary>
    /// Defines the result produced when a pre-condition or post-condition returns false.
    /// </summary>
    public enum ConditionPolicy : byte
    {
        /// <summary>Return Failure without executing, or abort a running activation and return Failure.</summary>
        FailWhenFalse,
        /// <summary>Return Success without executing, or abort a running activation and return Success.</summary>
        SucceedWhenFalse,
        /// <summary>Abort a running activation and return Failure. Before activation this is equivalent to FailWhenFalse.</summary>
        AbortWhenFalse,
    }

    /// <summary>
    /// Compact condition entry stored in a setup-time array for allocation-free iteration.
    /// </summary>
    public struct NodeCondition
    {
        public Func<RuntimeBlackboard, bool> Check;
        public ConditionPolicy Policy;
    }

    /// <summary>
    /// Base class for managed behavior-tree nodes.
    /// A node activation has exactly one exit: Completed, Aborted, or Faulted.
    /// Reset is explicit and is not implied by normal completion or abort.
    /// </summary>
    public abstract class RuntimeNode
    {
        public RuntimeState State { get; protected set; } = RuntimeState.NotEntered;
        public bool IsStarted { get; private set; }

        private string _guid;

        /// <summary>
        /// Stable setup-time identity used by diagnostics and synchronization.
        /// The identity is immutable once a tree owns this node.
        /// </summary>
        public string GUID
        {
            get => _guid;
            set
            {
                ThrowIfSetupFrozen();
                _guid = value;
            }
        }

        internal RuntimeBehaviorTree OwnerTree { get; set; }
        internal bool IsDisposed => _isDisposed;

        private NodeCondition[] _preConditions;
        private int _preConditionCount;
        private NodeCondition[] _postConditions;
        private int _postConditionCount;
        private bool _isDisposed;

        public virtual bool CanEvaluate => false;

        public virtual bool Evaluate(RuntimeBlackboard blackboard) => true;

        public virtual void OnAwake() { }

        internal void ValidateSetupNode()
        {
            ThrowIfDisposed();
            ValidateSetup();
        }

        /// <summary>
        /// Attaches a pre-condition evaluated before each execution step.
        /// Conditions are evaluated in registration order and the first false result wins.
        /// </summary>
        public void AddPreCondition(
            Func<RuntimeBlackboard, bool> check,
            ConditionPolicy policy = ConditionPolicy.FailWhenFalse)
        {
            ThrowIfSetupFrozen();
            if (check == null)
            {
                throw new ArgumentNullException(nameof(check));
            }

            if (_preConditions == null)
            {
                _preConditions = new NodeCondition[2];
            }
            else if (_preConditionCount >= _preConditions.Length)
            {
                Array.Resize(ref _preConditions, _preConditions.Length * 2);
            }

            _preConditions[_preConditionCount++] = new NodeCondition { Check = check, Policy = policy };
        }

        /// <summary>
        /// Attaches a post-condition evaluated after OnRun returns Success or Failure.
        /// Conditions are evaluated in registration order and the first false result wins.
        /// </summary>
        public void AddPostCondition(
            Func<RuntimeBlackboard, bool> check,
            ConditionPolicy policy = ConditionPolicy.FailWhenFalse)
        {
            ThrowIfSetupFrozen();
            if (check == null)
            {
                throw new ArgumentNullException(nameof(check));
            }

            if (_postConditions == null)
            {
                _postConditions = new NodeCondition[2];
            }
            else if (_postConditionCount >= _postConditions.Length)
            {
                Array.Resize(ref _postConditions, _postConditions.Length * 2);
            }

            _postConditions[_postConditionCount++] = new NodeCondition { Check = check, Policy = policy };
        }

        public RuntimeState Run(RuntimeBlackboard blackboard)
        {
            ThrowIfDisposed();

            try
            {
                RuntimeState conditionResult = EvaluateConditions(_preConditions, _preConditionCount, blackboard);
                if (conditionResult != RuntimeState.NotEntered)
                {
                    if (IsStarted)
                    {
                        AbortCore(blackboard);
                    }

                    State = conditionResult;
                    return State;
                }

                if (!IsStarted)
                {
                    IsStarted = true;
                    State = RuntimeState.Running;
                    OnStart(blackboard);
                }

                RuntimeState previousState = State;
                RuntimeState nextState = OnRun(blackboard);
                if (nextState == RuntimeState.NotEntered ||
                    (nextState != RuntimeState.Running &&
                     nextState != RuntimeState.Success &&
                     nextState != RuntimeState.Failure))
                {
                    throw new InvalidOperationException(
                        $"{GetType().FullName}.OnRun returned the invalid execution state {nextState}.");
                }

                State = nextState;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (State != previousState && OwnerTree?.StatusLogger != null)
                {
                    OwnerTree.StatusLogger.Log(GUID, previousState, State, RuntimeBTTime.GetTime(blackboard, false));
                }
#endif

                if (State == RuntimeState.Success || State == RuntimeState.Failure)
                {
                    RuntimeState postConditionResult = EvaluateConditions(
                        _postConditions,
                        _postConditionCount,
                        blackboard);
                    if (postConditionResult != RuntimeState.NotEntered)
                    {
                        State = postConditionResult;
                    }

                    IsStarted = false;
                    OnExit(blackboard, RuntimeNodeExitReason.Completed, null);
                }

                return State;
            }
            catch (Exception exception)
            {
                if (IsStarted)
                {
                    IsStarted = false;
                    State = RuntimeState.Failure;
                    InvokeFaultExit(blackboard, exception);
                }
                else if (State != RuntimeState.Failure)
                {
                    State = RuntimeState.Failure;
                }

                throw;
            }
        }

        public void Abort(RuntimeBlackboard blackboard)
        {
            ThrowIfDisposed();
            if (!IsStarted)
            {
                return;
            }

            try
            {
                AbortCore(blackboard);
            }
            catch
            {
                IsStarted = false;
                State = RuntimeState.Failure;
                throw;
            }
        }

        /// <summary>
        /// Aborts an active activation, clears persistent node state, and recursively resets descendants.
        /// </summary>
        public void Reset(RuntimeBlackboard blackboard)
        {
            ThrowIfDisposed();

            try
            {
                if (IsStarted)
                {
                    AbortCore(blackboard);
                }

                State = RuntimeState.NotEntered;
                OnReset(blackboard);
            }
            catch
            {
                IsStarted = false;
                State = RuntimeState.Failure;
                throw;
            }
        }

        internal void PrepareForActivation()
        {
            ThrowIfDisposed();
            if (IsStarted)
            {
                throw new InvalidOperationException("A running node cannot be prepared for a new activation.");
            }

            State = RuntimeState.NotEntered;
        }

        internal void DisposeNode(RuntimeBlackboard blackboard)
        {
            if (_isDisposed)
            {
                return;
            }

            Exception abortException = null;
            if (IsStarted)
            {
                try
                {
                    AbortCore(blackboard);
                }
                catch (Exception exception)
                {
                    abortException = exception;
                    IsStarted = false;
                    State = RuntimeState.Failure;
                }
            }

            _isDisposed = true;
            OwnerTree = null;

            try
            {
                OnDispose(blackboard);
            }
            catch (Exception disposeException)
            {
                if (abortException != null)
                {
                    throw new AggregateException(abortException, disposeException);
                }

                throw;
            }

            if (abortException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(abortException).Throw();
            }
        }

        /// <summary>
        /// Signals the owning tree to schedule a tick. This is the only node lifecycle operation
        /// intended to be called from a producer thread other than the tree owner thread.
        /// </summary>
        protected void EmitWakeUpSignal()
        {
            OwnerTree?.RequestWakeUp(2);
        }

        protected virtual void OnStart(RuntimeBlackboard blackboard) { }
        protected abstract RuntimeState OnRun(RuntimeBlackboard blackboard);

        /// <summary>
        /// Called exactly once for a started activation. Faulted exits include the triggering exception.
        /// State retains Success or Failure for Completed, NotEntered for Aborted, and Failure for Faulted.
        /// </summary>
        protected virtual void OnExit(
            RuntimeBlackboard blackboard,
            RuntimeNodeExitReason reason,
            Exception exception)
        {
        }

        protected virtual void OnReset(RuntimeBlackboard blackboard) { }

        protected virtual void OnDispose(RuntimeBlackboard blackboard) { }

        /// <summary>
        /// Validates cross-property setup invariants before ownership is committed.
        /// Implementations must not mutate node state or external resources.
        /// </summary>
        protected virtual void ValidateSetup() { }

        protected void SetSetupValue<T>(ref T storage, T value)
        {
            ThrowIfSetupFrozen();
            storage = value;
        }

        protected static void ValidateFiniteNonNegativeSetupValue(float value, string propertyName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    propertyName,
                    value,
                    "Value must be finite and non-negative.");
            }
        }

        /// <summary>
        /// Rejects setup mutation after ownership transfer or disposal.
        /// Runtime state changes must use explicit execution APIs instead.
        /// </summary>
        protected void ThrowIfSetupFrozen()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }

            if (OwnerTree != null)
            {
                throw new InvalidOperationException(
                    $"{GetType().FullName} setup is immutable after the node is owned by a runtime tree.");
            }
        }

        private RuntimeState EvaluateConditions(
            NodeCondition[] conditions,
            int count,
            RuntimeBlackboard blackboard)
        {
            for (int i = 0; i < count; i++)
            {
                ref NodeCondition condition = ref conditions[i];
                if (condition.Check(blackboard))
                {
                    continue;
                }

                switch (condition.Policy)
                {
                    case ConditionPolicy.FailWhenFalse:
                    case ConditionPolicy.AbortWhenFalse:
                        return RuntimeState.Failure;
                    case ConditionPolicy.SucceedWhenFalse:
                        return RuntimeState.Success;
                    default:
                        throw new InvalidOperationException($"Unsupported condition policy {condition.Policy}.");
                }
            }

            return RuntimeState.NotEntered;
        }

        private void AbortCore(RuntimeBlackboard blackboard)
        {
            IsStarted = false;
            State = RuntimeState.NotEntered;
            OnExit(blackboard, RuntimeNodeExitReason.Aborted, null);
        }

        private void InvokeFaultExit(RuntimeBlackboard blackboard, Exception exception)
        {
            try
            {
                OnExit(blackboard, RuntimeNodeExitReason.Faulted, exception);
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException(exception, cleanupException);
            }
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(GetType().FullName);
            }
        }
    }

    /// <summary>
    /// Base class for asynchronous or polling actions with explicit start, running, halt, and fault hooks.
    /// </summary>
    public abstract class RuntimeStatefulActionNode : RuntimeNode
    {
        private bool _wasRunning;

        protected abstract RuntimeState OnActionStart(RuntimeBlackboard blackboard);
        protected abstract RuntimeState OnActionRunning(RuntimeBlackboard blackboard);
        protected virtual void OnActionHalted(RuntimeBlackboard blackboard) { }
        protected virtual void OnActionFaulted(RuntimeBlackboard blackboard, Exception exception) { }

        protected sealed override void OnStart(RuntimeBlackboard blackboard)
        {
            _wasRunning = false;
        }

        protected sealed override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            if (!_wasRunning)
            {
                RuntimeState result = OnActionStart(blackboard);
                _wasRunning = result == RuntimeState.Running;
                return result;
            }

            RuntimeState state = OnActionRunning(blackboard);
            if (state != RuntimeState.Running)
            {
                _wasRunning = false;
            }

            return state;
        }

        protected sealed override void OnExit(
            RuntimeBlackboard blackboard,
            RuntimeNodeExitReason reason,
            Exception exception)
        {
            if (reason == RuntimeNodeExitReason.Aborted && _wasRunning)
            {
                OnActionHalted(blackboard);
            }
            else if (reason == RuntimeNodeExitReason.Faulted)
            {
                OnActionFaulted(blackboard, exception);
            }

            _wasRunning = false;
        }

        protected sealed override void OnReset(RuntimeBlackboard blackboard)
        {
            _wasRunning = false;
        }
    }
}
