using System;

namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators
{
    /// <summary>
    /// Retries child on FAILURE up to N times. Returns SUCCESS on child success,
    /// FAILURE after all attempts are exhausted. Use MaxAttempts = -1 for infinite retry.
    /// </summary>
    public class RuntimeRetryNode : RuntimeDecoratorNode
    {
        private int _maxAttempts = 3;

        public int MaxAttempts
        {
            get => _maxAttempts;
            set
            {
                ThrowIfSetupFrozen();
                if (value != -1 && value < 1)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(MaxAttempts),
                        value,
                        "Maximum attempts must be -1 or at least 1.");
                }
                _maxAttempts = value;
            }
        }
        private int _attemptCount;

        protected override void OnStart(RuntimeBlackboard blackboard)
        {
            _attemptCount = 0;
        }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            if (Child == null) return RuntimeState.Failure;

            var state = Child.Run(blackboard);

            if (state == RuntimeState.Success)
                return RuntimeState.Success;

            if (state == RuntimeState.Failure)
            {
                _attemptCount++;
                if (MaxAttempts >= 0 && _attemptCount >= MaxAttempts)
                    return RuntimeState.Failure;

                return RuntimeState.Running;
            }

            return RuntimeState.Running;
        }
    }
}
