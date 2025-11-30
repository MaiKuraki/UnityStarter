using CycloneGames.BehaviorTree.Runtime.Data;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Compositors
{
    public class SimpleParallelNode : CompositeNode
    {
        protected override BTState OnDeActiveEvaluate(BlackBoard blackBoard)
        {
            return BTState.SUCCESS;
        }
        protected override BTState OnActiveEvaluate(BlackBoard blackBoard)
        {
            return BTState.SUCCESS;
        }
        public override BTState Evaluate(BlackBoard blackBoard)
        {
            for (int i = 0; i < Children.Count; i++)
            {
                if (!Children[i].CanReEvaluate) continue;
                var result = Children[i].Evaluate(blackBoard);
                if (result == BTState.FAILURE) return BTState.FAILURE;
            }
            return BTState.SUCCESS;
        }
        protected override BTState RunChildren(BlackBoard blackBoard)
        {
            for (int i = 0; i < Children.Count; i++)
            {
                Children[i].Run(blackBoard);
            }
            return BTState.SUCCESS;
        }
    }
}