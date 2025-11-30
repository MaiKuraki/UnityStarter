using CycloneGames.BehaviorTree.Runtime.Data;
using CycloneGames.BehaviorTree.Runtime.Nodes;

namespace CycloneGames.BehaviorTree.Runtime.Conditions
{
    public abstract class ConditionNode : BTNode
    {
        public override bool CanReEvaluate => true;
        public override BTState Evaluate(BlackBoard blackBoard) => GetConditionState(blackBoard);

        protected override BTState OnRun(BlackBoard blackBoard) => GetConditionState(blackBoard);
        protected abstract BTState GetConditionState(BlackBoard blackBoard);
    }
}