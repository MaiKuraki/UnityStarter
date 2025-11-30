using CycloneGames.BehaviorTree.Runtime.Data;
using CycloneGames.BehaviorTree.Runtime.Interfaces;
using CycloneGames.BehaviorTree.Runtime.Nodes;

namespace CycloneGames.BehaviorTree.Runtime.Conditions
{
    public abstract class ConditionNode : BTNode
    {
        public override bool CanReEvaluate => true;
        public override BTState Evaluate(IBlackBoard blackBoard) => GetConditionState(blackBoard);

        protected override BTState OnRun(IBlackBoard blackBoard) => GetConditionState(blackBoard);
        protected abstract BTState GetConditionState(IBlackBoard blackBoard);
    }
}