namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators
{
    public class RuntimeSucceederNode : RuntimeDecoratorNode
    {
        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            if (Child == null) return RuntimeState.Success;

            var state = Child.Run(blackboard);

            if (state == RuntimeState.Failure)
            {
                return RuntimeState.Success;
            }
            return state;
        }
    }
}
