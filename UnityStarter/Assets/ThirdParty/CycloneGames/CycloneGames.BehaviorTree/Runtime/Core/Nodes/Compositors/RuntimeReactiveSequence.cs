namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors
{
    /// <summary>
    /// Ticks ALL children from first to last every tick (reactive re-evaluation).
    /// If any child returns RUNNING → halt later siblings, return RUNNING.
    /// If any child returns FAILURE → halt later siblings, return FAILURE.
    /// All SUCCESS → return SUCCESS.
    /// Unlike standard Sequencer, this always re-evaluates from child[0].
    /// </summary>
    public class RuntimeReactiveSequence : RuntimeCompositeNode
    {
        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            var children = Children;
            for (int i = 0; i < children.Length; i++)
            {
                var state = children[i].Run(blackboard);

                if (state == RuntimeState.Failure)
                {
                    // Abort any later running children
                    for (int j = i + 1; j < children.Length; j++)
                    {
                        if (children[j].IsStarted) children[j].Abort(blackboard);
                    }
                    return RuntimeState.Failure;
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
            return RuntimeState.Success;
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
