namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators
{
    /// <summary>
    /// Returns RUNNING as long as child returns SUCCESS.
    /// Only returns FAILURE when child returns FAILURE, effectively
    /// keeping the node alive in the tree until failure occurs.
    /// </summary>
    public class RuntimeKeepRunningUntilFailureNode : RuntimeDecoratorNode
    {
        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            if (Child == null) return RuntimeState.Failure;

            var state = Child.Run(blackboard);

            if (state == RuntimeState.Failure) return RuntimeState.Failure;

            if (state == RuntimeState.Success)
            {
                Child.Abort(blackboard);
            }

            return RuntimeState.Running;
        }
    }
}
