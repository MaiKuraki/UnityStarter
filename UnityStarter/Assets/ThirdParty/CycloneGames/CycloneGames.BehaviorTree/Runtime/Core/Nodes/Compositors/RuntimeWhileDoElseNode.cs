namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors
{
    /// <summary>
    /// While-do-else: expects 2-3 children [condition, do_action, else_action?].
    /// While condition succeeds, tick do_action.
    /// When condition fails, tick else_action (if present), then return FAILURE.
    /// </summary>
    public class RuntimeWhileDoElseNode : RuntimeCompositeNode
    {
        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            var children = Children;
            if (children == null || children.Length < 2) return RuntimeState.Failure;

            var condState = children[0].Run(blackboard);

            if (condState == RuntimeState.Running)
                return RuntimeState.Running;

            if (condState == RuntimeState.Success)
            {
                // Abort else branch if it was running
                if (children.Length >= 3 && children[2].IsStarted)
                    children[2].Abort(blackboard);

                var doState = children[1].Run(blackboard);
                if (doState == RuntimeState.Success)
                {
                    // Reset condition and do to repeat the loop
                    children[0].Abort(blackboard);
                    children[1].Abort(blackboard);
                    return RuntimeState.Running;
                }
                return doState;
            }

            // Condition FAILURE: run else branch if available
            if (children[1].IsStarted) children[1].Abort(blackboard);

            if (children.Length >= 3)
            {
                return children[2].Run(blackboard);
            }

            return RuntimeState.Failure;
        }

        protected override void OnStop(RuntimeBlackboard blackboard)
        {
            var children = Children;
            if (children == null) return;
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].IsStarted) children[i].Abort(blackboard);
            }
        }
    }
}
