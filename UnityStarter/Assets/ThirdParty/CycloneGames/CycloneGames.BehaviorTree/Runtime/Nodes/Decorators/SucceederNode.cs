using CycloneGames.BehaviorTree.Runtime.Data;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Decorators
{
    public class SucceederNode : DecoratorNode
    {
        protected override BTState OnRun(BlackBoard blackBoard)
        {
            var childState = Child.Run(blackBoard);
            if (childState == BTState.RUNNING)
            {
                return BTState.RUNNING;
            }
            return BTState.SUCCESS;
        }
    }
}