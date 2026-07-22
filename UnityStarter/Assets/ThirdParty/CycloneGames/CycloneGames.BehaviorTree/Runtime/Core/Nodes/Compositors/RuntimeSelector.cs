namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors
{
    public class RuntimeSelector : RuntimeCompositeNode
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
            if (children == null || children.Length == 0)
            {
                return RuntimeState.Failure;
            }

            // LowerPriority / Both: check if a higher-priority sibling should take over
            if (_current > 0 && (AbortType == RuntimeAbortType.LowerPriority || AbortType == RuntimeAbortType.Both))
            {
                for (int i = 0; i < _current; i++)
                {
                    if (children[i].CanEvaluate && children[i].Evaluate(blackboard))
                    {
                        if (children[_current].IsStarted)
                            children[_current].Abort(blackboard);
                        _current = i;
                        return children[_current].Run(blackboard);
                    }
                }
            }

            // Self / Both: check if current child's conditions still hold
            if (AbortType == RuntimeAbortType.Self || AbortType == RuntimeAbortType.Both)
            {
                if (children[_current].IsStarted && children[_current].CanEvaluate
                    && !children[_current].Evaluate(blackboard))
                {
                    children[_current].Abort(blackboard);
                    _current++;
                    // Fall through to normal selector loop from new _current
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

                if (state == RuntimeState.Success)
                {
                    return RuntimeState.Success;
                }
            }

            return RuntimeState.Failure;
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
