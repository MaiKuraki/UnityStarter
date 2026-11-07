namespace CycloneGames.BehaviorTree.Core.Nodes.Compositors
{
    public class RuntimeWhileDoElseNode : RuntimeCompositeNode
    {
        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            RuntimeNode[] children = ChildArray;
            if (children == null || children.Length < 2) return RuntimeState.Failure;

            var condState = children[0].Run(blackboard);

            if (condState == RuntimeState.Running)
                return RuntimeState.Running;

            if (condState == RuntimeState.Success)
            {
                if (children.Length >= 3 && children[2].IsStarted)
                    children[2].Abort(blackboard);

                var doState = children[1].Run(blackboard);
                if (doState == RuntimeState.Success)
                {
                    children[0].Abort(blackboard);
                    children[1].Abort(blackboard);
                    return RuntimeState.Running;
                }
                return doState;
            }

            if (children[1].IsStarted) children[1].Abort(blackboard);

            if (children.Length >= 3)
            {
                return children[2].Run(blackboard);
            }

            return RuntimeState.Failure;
        }

        protected override void OnExit(RuntimeBlackboard blackboard, RuntimeNodeExitReason reason, System.Exception exception)
        {
            RuntimeNode[] children = ChildArray;
            if (children == null) return;
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].IsStarted) children[i].Abort(blackboard);
            }
        }
    }
}
