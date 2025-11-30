using CycloneGames.BehaviorTree.Runtime.Data;
using CycloneGames.BehaviorTree.Runtime.Interfaces;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Actions
{
    public abstract class ActionNode : BTNode
    {
        public override bool CanReEvaluate => false;
        public override BTState Evaluate(IBlackBoard blackBoard) => BTState.SUCCESS;
    }
}