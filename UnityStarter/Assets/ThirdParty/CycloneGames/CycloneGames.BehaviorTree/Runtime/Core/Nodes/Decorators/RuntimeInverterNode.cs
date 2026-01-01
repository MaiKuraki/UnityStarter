namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators
{
    public class RuntimeInverterNode : RuntimeDecoratorNode
    {
        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            if (Child == null) return RuntimeState.Failure;

            var state = Child.Run(blackboard);

            switch (state)
            {
                case RuntimeState.Success: return RuntimeState.Failure;
                case RuntimeState.Failure: return RuntimeState.Success;
                case RuntimeState.Running: return RuntimeState.Running;
                default: return RuntimeState.Failure;
            }
        }
    }
}
