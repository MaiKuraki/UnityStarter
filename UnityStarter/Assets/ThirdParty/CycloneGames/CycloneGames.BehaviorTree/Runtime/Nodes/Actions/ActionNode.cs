using CycloneGames.BehaviorTree.Runtime.Data;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Actions
{
    public abstract class ActionNode : BTNode
    {
        public override bool CanReEvaluate => false;
        public override BTState Evaluate(BlackBoard blackBoard) => BTState.SUCCESS;
    }
}