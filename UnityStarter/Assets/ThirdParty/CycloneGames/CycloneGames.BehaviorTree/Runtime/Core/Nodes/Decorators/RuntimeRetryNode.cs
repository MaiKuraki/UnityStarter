namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators
{
    /// <summary>
    /// Retries child on FAILURE up to N times. Returns SUCCESS on child success,
    /// FAILURE after all attempts exhausted. Use RepeatCount = -1 for infinite retry.
    /// </summary>
    public class RuntimeRetryNode : RuntimeDecoratorNode
    {
        public int MaxAttempts { get; set; } = 3;
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

                Child.Abort(blackboard);
                return RuntimeState.Running;
            }

            return RuntimeState.Running;
        }
    }
}
