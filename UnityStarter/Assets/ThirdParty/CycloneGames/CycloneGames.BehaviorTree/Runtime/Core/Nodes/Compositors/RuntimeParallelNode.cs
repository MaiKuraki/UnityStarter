using System;

namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors
{
    public enum RuntimeParallelMode
    {
        Default,
        UntilAnyComplete,
        UntilAnyFailure,
        UntilAnySuccess,
    }

    /// <summary>
    /// Runs child branches concurrently on the tree owner thread.
    /// Completed children are retained in a setup-time state array and are not executed again
    /// until the parallel node starts a new activation.
    /// </summary>
    public class RuntimeParallelNode : RuntimeCompositeNode
    {
        private RuntimeState[] _childStates = Array.Empty<RuntimeState>();
        private RuntimeParallelMode _mode = RuntimeParallelMode.Default;

        public RuntimeParallelMode Mode
        {
            get => _mode;
            set
            {
                ThrowIfSetupFrozen();
                if ((uint)(int)value > (uint)RuntimeParallelMode.UntilAnySuccess)
                {
                    throw new ArgumentOutOfRangeException(nameof(Mode), value, "Unsupported parallel mode.");
                }
                _mode = value;
            }
        }

        public override void OnAwake()
        {
            base.OnAwake();
            _childStates = ChildCount == 0
                ? Array.Empty<RuntimeState>()
                : new RuntimeState[ChildCount];
        }

        protected override void OnStart(RuntimeBlackboard blackboard)
        {
            RuntimeNode[] children = ChildArray;
            Array.Clear(_childStates, 0, _childStates.Length);
            for (int i = 0; i < children.Length; i++)
            {
                children[i].PrepareForActivation();
            }
        }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            RuntimeNode[] children = ChildArray;
            if (children == null || children.Length == 0)
            {
                return RuntimeState.Failure;
            }

            int successCount = 0;
            int failureCount = 0;

            for (int i = 0; i < children.Length; i++)
            {
                RuntimeState childState = _childStates[i];
                if (childState != RuntimeState.Success && childState != RuntimeState.Failure)
                {
                    childState = children[i].Run(blackboard);
                    _childStates[i] = childState;
                }

                if (childState == RuntimeState.Success)
                {
                    successCount++;
                    if (Mode == RuntimeParallelMode.UntilAnyComplete ||
                        Mode == RuntimeParallelMode.UntilAnySuccess)
                    {
                        AbortRunningChildren(blackboard);
                        return RuntimeState.Success;
                    }
                }
                else if (childState == RuntimeState.Failure)
                {
                    failureCount++;
                    if (Mode == RuntimeParallelMode.Default ||
                        Mode == RuntimeParallelMode.UntilAnyComplete ||
                        Mode == RuntimeParallelMode.UntilAnyFailure)
                    {
                        AbortRunningChildren(blackboard);
                        return RuntimeState.Failure;
                    }
                }
            }

            switch (Mode)
            {
                case RuntimeParallelMode.Default:
                case RuntimeParallelMode.UntilAnyFailure:
                    return successCount == children.Length
                        ? RuntimeState.Success
                        : RuntimeState.Running;
                case RuntimeParallelMode.UntilAnySuccess:
                    return failureCount == children.Length
                        ? RuntimeState.Failure
                        : RuntimeState.Running;
                case RuntimeParallelMode.UntilAnyComplete:
                    return RuntimeState.Running;
                default:
                    throw new InvalidOperationException($"Unsupported parallel mode {Mode}.");
            }
        }

        protected override void OnReset(RuntimeBlackboard blackboard)
        {
            base.OnReset(blackboard);
            Array.Clear(_childStates, 0, _childStates.Length);
        }
    }
}
