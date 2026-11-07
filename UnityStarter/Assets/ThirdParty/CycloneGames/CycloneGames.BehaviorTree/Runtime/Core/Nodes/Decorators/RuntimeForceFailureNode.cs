namespace CycloneGames.BehaviorTree.Core.Nodes.Decorators
{
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
