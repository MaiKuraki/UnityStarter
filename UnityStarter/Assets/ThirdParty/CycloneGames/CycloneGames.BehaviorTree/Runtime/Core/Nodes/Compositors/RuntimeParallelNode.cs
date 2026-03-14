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
            var children = Children;
            for (int i = 0; i < children.Length; i++)
            {
                children[i].Run(blackboard);
            }
            return RuntimeState.Running;
        }

        private RuntimeState RunUntilAnyComplete(RuntimeBlackboard blackboard)
        {
            var children = Children;
            for (int i = 0; i < children.Length; i++)
            {
                var state = children[i].Run(blackboard);
                if (state == RuntimeState.Success || state == RuntimeState.Failure)
                {
                    return RuntimeState.Success;
                }
            }
            return RuntimeState.Running;
        }

        private RuntimeState RunUntilAnyFailure(RuntimeBlackboard blackboard)
        {
            var children = Children;
            for (int i = 0; i < children.Length; i++)
            {
                var state = children[i].Run(blackboard);
                if (state == RuntimeState.Failure) return RuntimeState.Success;
            }
            return RuntimeState.Running;
        }

        private RuntimeState RunUntilAnySuccess(RuntimeBlackboard blackboard)
        {
            var children = Children;
            for (int i = 0; i < children.Length; i++)
            {
                var state = children[i].Run(blackboard);
                if (state == RuntimeState.Success) return RuntimeState.Success;
            }
            return RuntimeState.Running;
        }

        protected override void OnStop(RuntimeBlackboard blackboard)
        {
            var children = Children;
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].IsStarted)
                {
                    children[i].Abort(blackboard);
                }
            }
        }
    }
}
