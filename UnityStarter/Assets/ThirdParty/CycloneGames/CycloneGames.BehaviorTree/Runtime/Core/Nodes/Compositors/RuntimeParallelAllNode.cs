using System;

namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors
{
    /// <summary>
    /// Ticks every unfinished child once per step and completes when a configured threshold is met.
    /// Failure has precedence if success and failure thresholds are reached in the same step.
    /// </summary>
    public class RuntimeParallelAllNode : RuntimeCompositeNode
    {
        private RuntimeState[] _childStates = Array.Empty<RuntimeState>();
        private int _effectiveSuccessThreshold;
        private int _effectiveFailureThreshold;
        private int _successThreshold = -1;
        private int _failureThreshold = 1;

        /// <summary>Number of successful children required. -1 means all children.</summary>
        public int SuccessThreshold
        {
            get => _successThreshold;
            set
            {
                ThrowIfSetupFrozen();
                ValidateThresholdDomain(value, nameof(SuccessThreshold));
                _successThreshold = value;
            }
        }

        /// <summary>Number of failed children required. -1 means all children.</summary>
        public int FailureThreshold
        {
            get => _failureThreshold;
            set
            {
                ThrowIfSetupFrozen();
                ValidateThresholdDomain(value, nameof(FailureThreshold));
                _failureThreshold = value;
            }
        }

        protected override void ValidateSetup()
        {
            int childCount = ChildCount;
            if (childCount == 0)
            {
                return;
            }

            int success = ResolveThreshold(SuccessThreshold, childCount, nameof(SuccessThreshold));
            int failure = ResolveThreshold(FailureThreshold, childCount, nameof(FailureThreshold));
            if (success + failure > childCount + 1)
            {
                throw new InvalidOperationException(
                    "Parallel thresholds can leave a fully completed node without a terminal result.");
            }
        }

        public override void OnAwake()
        {
            base.OnAwake();

            int childCount = ChildCount;
            _childStates = childCount == 0
                ? Array.Empty<RuntimeState>()
                : new RuntimeState[childCount];

            ConfigureThresholds(childCount);
        }

        protected override void OnStart(RuntimeBlackboard blackboard)
        {
            RuntimeNode[] children = ChildArray;
            ConfigureThresholds(children.Length);
            Array.Clear(_childStates, 0, _childStates.Length);
            for (int i = 0; i < children.Length; i++)
            {
                children[i].PrepareForActivation();
            }
        }

        private void ConfigureThresholds(int childCount)
        {
            if (childCount == 0)
            {
                _effectiveSuccessThreshold = 0;
                _effectiveFailureThreshold = 1;
                return;
            }

            _effectiveSuccessThreshold = ResolveThreshold(SuccessThreshold, childCount, nameof(SuccessThreshold));
            _effectiveFailureThreshold = ResolveThreshold(FailureThreshold, childCount, nameof(FailureThreshold));

            if (_effectiveSuccessThreshold + _effectiveFailureThreshold > childCount + 1)
            {
                throw new InvalidOperationException(
                    "Parallel thresholds can leave a fully completed node without a terminal result.");
            }
        }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            RuntimeNode[] children = ChildArray;
            if (children == null || children.Length == 0)
            {
                return RuntimeState.Success;
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
                }
                else if (childState == RuntimeState.Failure)
                {
                    failureCount++;
                }
            }

            if (failureCount >= _effectiveFailureThreshold)
            {
                AbortRunningChildren(blackboard);
                return RuntimeState.Failure;
            }

            if (successCount >= _effectiveSuccessThreshold)
            {
                AbortRunningChildren(blackboard);
                return RuntimeState.Success;
            }

            return successCount + failureCount == children.Length
                ? RuntimeState.Failure
                : RuntimeState.Running;
        }

        protected override void OnReset(RuntimeBlackboard blackboard)
        {
            base.OnReset(blackboard);
            Array.Clear(_childStates, 0, _childStates.Length);
        }

        private static int ResolveThreshold(int configured, int childCount, string propertyName)
        {
            if (configured == -1)
            {
                return childCount;
            }

            if (configured < 1 || configured > childCount)
            {
                throw new ArgumentOutOfRangeException(
                    propertyName,
                    configured,
                    $"Threshold must be -1 or between 1 and the child count ({childCount}).");
            }

            return configured;
        }

        private static void ValidateThresholdDomain(int configured, string propertyName)
        {
            if (configured != -1 && configured < 1)
            {
                throw new ArgumentOutOfRangeException(
                    propertyName,
                    configured,
                    "Threshold must be -1 or at least 1.");
            }
        }
    }
}
