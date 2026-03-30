namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors
{
    /// <summary>
    /// Ticks ALL children from first to last every tick (reactive re-evaluation).
    /// If any child returns SUCCESS → halt later siblings, return SUCCESS.
    /// If any child returns RUNNING → halt later siblings, return RUNNING.
    /// All FAILURE → return FAILURE.
    /// Unlike standard Selector, this always re-evaluates from child[0].
    /// </summary>
    public class RuntimeReactiveFallback : RuntimeCompositeNode
    {
        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            var children = Children;
            for (int i = 0; i < children.Length; i++)
            {
                var state = children[i].Run(blackboard);

                if (state == RuntimeState.Success)
                {
                    for (int j = i + 1; j < children.Length; j++)
                    {
                        if (children[j].IsStarted) children[j].Abort(blackboard);
                    }
                    return RuntimeState.Success;
                }

                if (state == RuntimeState.Running)
                {
                    for (int j = i + 1; j < children.Length; j++)
                    {
                        if (children[j].IsStarted) children[j].Abort(blackboard);
                    }
                    return RuntimeState.Running;
                }
            }
            return RuntimeState.Failure;
        }

        protected override void OnStop(RuntimeBlackboard blackboard)
        {
            var children = Children;
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].IsStarted) children[i].Abort(blackboard);
            }
        }
    }
}
