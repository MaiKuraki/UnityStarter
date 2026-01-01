namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors
{
    public enum RuntimeParallelMode
    {
        Default,
        UntilAnyComplete,
        UntilAnyFailure,
        UntilAnySuccess,
    }

    public class RuntimeParallelNode : RuntimeCompositeNode
    {
        public RuntimeParallelMode Mode { get; set; } = RuntimeParallelMode.Default;

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            switch (Mode)
            {
                case RuntimeParallelMode.Default:
                    return RunDefault(blackboard);
                case RuntimeParallelMode.UntilAnyComplete:
                    return RunUntilAnyComplete(blackboard);
                case RuntimeParallelMode.UntilAnyFailure:
                    return RunUntilAnyFailure(blackboard);
                case RuntimeParallelMode.UntilAnySuccess:
                    return RunUntilAnySuccess(blackboard);
                default:
                    return RuntimeState.Failure;
            }
        }

        private RuntimeState RunDefault(RuntimeBlackboard blackboard)
        {
            for (int i = 0; i < Children.Count; i++)
            {
                Children[i].Run(blackboard);
            }
            return RuntimeState.Running;
        }

        private RuntimeState RunUntilAnyComplete(RuntimeBlackboard blackboard)
        {
            for (int i = 0; i < Children.Count; i++)
            {
                var state = Children[i].Run(blackboard);
                if (state == RuntimeState.Success || state == RuntimeState.Failure)
                {
                    return RuntimeState.Success;
                }
            }
            return RuntimeState.Running;
        }

        private RuntimeState RunUntilAnyFailure(RuntimeBlackboard blackboard)
        {
            for (int i = 0; i < Children.Count; i++)
            {
                var state = Children[i].Run(blackboard);
                if (state == RuntimeState.Failure) return RuntimeState.Success;
            }
            return RuntimeState.Running;
        }

        private RuntimeState RunUntilAnySuccess(RuntimeBlackboard blackboard)
        {
            for (int i = 0; i < Children.Count; i++)
            {
                var state = Children[i].Run(blackboard);
                if (state == RuntimeState.Success) return RuntimeState.Success;
            }
            return RuntimeState.Running;
        }
    }
}
