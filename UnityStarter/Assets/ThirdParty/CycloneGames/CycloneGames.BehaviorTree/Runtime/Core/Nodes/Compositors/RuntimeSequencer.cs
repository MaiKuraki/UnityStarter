namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors
{
    public class RuntimeSequencer : RuntimeCompositeNode
    {
        private int _current;
        public override int CurrentIndex => _current;

        protected override void OnStart(RuntimeBlackboard blackboard)
        {
            _current = 0;
            // Reset children states from previous iteration so editor doesn't show stale results
            RuntimeNode[] children = ChildArray;
            for (int i = 0; i < children.Length; i++)
                children[i].PrepareForActivation();
        }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            RuntimeNode[] children = ChildArray;

            // Self / Both: re-evaluate conditions of previously completed children
            if (_current > 0 && (AbortType == RuntimeAbortType.Self || AbortType == RuntimeAbortType.Both))
            {
                for (int i = 0; i < _current; i++)
                {
                    if (children[i].CanEvaluate && !children[i].Evaluate(blackboard))
                    {
                        if (children[_current].IsStarted)
                            children[_current].Abort(blackboard);
                        return RuntimeState.Failure;
                    }
                }
            }

            for (int i = _current; i < children.Length; i++)
            {
                var state = children[i].Run(blackboard);

                if (state == RuntimeState.Running)
                {
                    _current = i;
                    return RuntimeState.Running;
                }

                if (state == RuntimeState.Failure)
                {
                    return RuntimeState.Failure;
                }
            }

            return RuntimeState.Success;
        }

        protected override void OnExit(RuntimeBlackboard blackboard, RuntimeNodeExitReason reason, System.Exception exception)
        {
            RuntimeNode[] children = ChildArray;
            if (_current < children.Length && children[_current].IsStarted)
            {
                children[_current].Abort(blackboard);
            }
        }
    }
}
