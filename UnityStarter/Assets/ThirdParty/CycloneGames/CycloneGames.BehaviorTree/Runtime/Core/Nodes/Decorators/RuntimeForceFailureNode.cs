namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators
{
    /// <summary>
    /// Forces child result to FAILURE regardless of actual outcome.
    /// Running is passed through.
    /// </summary>
    public class RuntimeForceFailureNode : RuntimeDecoratorNode
    {
        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            if (Child == null) return RuntimeState.Failure;

            var state = Child.Run(blackboard);
            return state == RuntimeState.Running ? RuntimeState.Running : RuntimeState.Failure;
        }
    }
}
