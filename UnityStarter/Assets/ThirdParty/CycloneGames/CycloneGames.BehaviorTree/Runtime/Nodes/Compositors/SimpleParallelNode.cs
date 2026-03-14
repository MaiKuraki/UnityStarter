using CycloneGames.BehaviorTree.Runtime.Data;
using CycloneGames.BehaviorTree.Runtime.Interfaces;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Compositors
{
    public class SimpleParallelNode : CompositeNode
    {
        protected override BTState OnDeActiveEvaluate(IBlackBoard blackBoard)
        {
            return BTState.SUCCESS;
        }
        protected override BTState OnActiveEvaluate(IBlackBoard blackBoard)
        {
            return BTState.SUCCESS;
        }
        public override BTState Evaluate(IBlackBoard blackBoard)
        {
            for (int i = 0; i < Children.Count; i++)
            {
                if (!Children[i].CanReEvaluate) continue;
                var result = Children[i].Evaluate(blackBoard);
                if (result == BTState.FAILURE) return BTState.FAILURE;
            }
            return BTState.SUCCESS;
        }
        protected override BTState RunChildren(IBlackBoard blackBoard)
        {
            for (int i = 0; i < Children.Count; i++)
            {
                Children[i].Run(blackBoard);
            }
            return BTState.SUCCESS;
        }

        public override CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode CreateRuntimeNode()
        {
            var node = new CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors.RuntimeParallelNode();
            node.GUID = GUID;
            node.Mode = CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors.RuntimeParallelMode.Default;
            node.AbortType = (CycloneGames.BehaviorTree.Runtime.Core.RuntimeAbortType)(int)AbortType;
            foreach (var child in Children)
            {
                if (child != null)
                {
                    node.AddChild(child.CreateRuntimeNode());
                }
            }
            return node;
        }
    }
}