using CycloneGames.BehaviorTree.Runtime.Data;
using CycloneGames.BehaviorTree.Runtime.Interfaces;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Decorators
{
    public class SucceederNode : DecoratorNode
    {
        protected override BTState OnRun(IBlackBoard blackBoard)
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