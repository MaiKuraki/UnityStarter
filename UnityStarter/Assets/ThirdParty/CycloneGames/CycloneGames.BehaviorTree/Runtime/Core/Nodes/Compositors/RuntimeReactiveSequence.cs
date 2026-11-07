namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors
{
    /// <summary>
    /// Ticks ALL children from first to last every tick (reactive re-evaluation).
    /// If any child returns RUNNING → halt later siblings, return RUNNING.
    /// If any child returns FAILURE → halt later siblings, return FAILURE.
    /// All SUCCESS → return SUCCESS.
    /// With OnChange or Hybrid reevaluation, ticks without a request advance only the
    /// active branch; a request resumes the full head-to-tail scan.
    /// </summary>
    public class RuntimeReactiveSequence : RuntimeCompositeNode
    {
        private int _activeChild = -1;

        protected override void OnStart(RuntimeBlackboard blackboard)
        {
            _activeChild = -1;
        }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            RuntimeNode[] children = ChildArray;
            bool reevaluate = ShouldReevaluate();

            if (ReevaluationMode != RuntimeReevaluationMode.PollEveryTick &&
                !reevaluate &&
                _activeChild >= 0 && _activeChild < children.Length)
            {
                RuntimeState activeState = children[_activeChild].Run(blackboard);
                if (activeState == RuntimeState.Running)
                {
                    return RuntimeState.Running;
                }

                _activeChild = -1;
            }

            for (int i = 0; i < children.Length; i++)
            {
                var state = children[i].Run(blackboard);

                if (state == RuntimeState.Failure)
                {
                    for (int j = i + 1; j < children.Length; j++)
                    {
                        if (children[j].IsStarted) children[j].Abort(blackboard);
                    }
                    _activeChild = -1;
                    return RuntimeState.Failure;
                }

                if (state == RuntimeState.Running)
                {
                    for (int j = i + 1; j < children.Length; j++)
                    {
                        if (children[j].IsStarted) children[j].Abort(blackboard);
                    }
                    _activeChild = i;
                    return RuntimeState.Running;
                }
            }

            _activeChild = -1;
            return RuntimeState.Success;
        }

        protected override void OnExit(RuntimeBlackboard blackboard, RuntimeNodeExitReason reason, System.Exception exception)
        {
            _activeChild = -1;
            RuntimeNode[] children = ChildArray;
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].IsStarted) children[i].Abort(blackboard);
            }
        }
    }
}
