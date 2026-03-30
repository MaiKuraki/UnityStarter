namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors
{
    /// <summary>
    /// Ticks all children each frame. Configurable success/failure thresholds.
    /// SuccessThreshold = -1 means ALL must succeed. FailureThreshold = 1 means
    /// one failure triggers FAILURE. Mirrors BehaviorTree.CPP's ParallelNode semantics.
    /// </summary>
    public class RuntimeParallelAllNode : RuntimeCompositeNode
    {
        /// <summary>Number of successful children needed to return SUCCESS. -1 = all.</summary>
        public int SuccessThreshold { get; set; } = -1;
        /// <summary>Number of failed children needed to return FAILURE. -1 = all.</summary>
        public int FailureThreshold { get; set; } = 1;

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            var children = Children;
            if (children == null || children.Length == 0) return RuntimeState.Success;

            int successCount = 0;
            int failureCount = 0;
            int totalChildren = children.Length;

            int effectiveSuccessThreshold = SuccessThreshold < 0 ? totalChildren : SuccessThreshold;
            int effectiveFailureThreshold = FailureThreshold < 0 ? totalChildren : FailureThreshold;

            for (int i = 0; i < totalChildren; i++)
            {
                var state = children[i].Run(blackboard);
                switch (state)
                {
                    case RuntimeState.Success:
                        successCount++;
                        break;
                    case RuntimeState.Failure:
                        failureCount++;
                        break;
                }
            }

            if (successCount >= effectiveSuccessThreshold)
            {
                AbortRunningChildren(blackboard);
                return RuntimeState.Success;
            }

            if (failureCount >= effectiveFailureThreshold)
            {
                AbortRunningChildren(blackboard);
                return RuntimeState.Failure;
            }

            return RuntimeState.Running;
        }

        private void AbortRunningChildren(RuntimeBlackboard blackboard)
        {
            var children = Children;
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].IsStarted) children[i].Abort(blackboard);
            }
        }

        protected override void OnStop(RuntimeBlackboard blackboard)
        {
            AbortRunningChildren(blackboard);
        }
    }
}
