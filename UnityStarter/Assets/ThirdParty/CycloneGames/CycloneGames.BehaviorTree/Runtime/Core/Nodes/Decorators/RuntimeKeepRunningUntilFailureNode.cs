namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators
{
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
