using CycloneGames.BehaviorTree.Runtime.Attributes;
using CycloneGames.BehaviorTree.Runtime.Data;
using CycloneGames.BehaviorTree.Runtime.Interfaces;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Compositors
{
    [BTInfo("Conditional", "If-Then-Else: Child[0] is condition, Child[1] is 'then', Child[2] is 'else'.")]
    public class IfThenElseNode : CompositeNode
    {
        protected override BTState OnActiveEvaluate(IBlackBoard blackBoard)
        {
            if (Children.Count == 0) return BTState.FAILURE;
            var condition = Children[0];
            if (condition == null) return BTState.FAILURE;
            if (!condition.CanReEvaluate) return BTState.SUCCESS;
            return condition.Evaluate(blackBoard);
        }

        protected override BTState RunChildren(IBlackBoard blackBoard)
        {
            if (Children.Count == 0) return BTState.FAILURE;

            var conditionResult = Children[0].Run(blackBoard);
            if (conditionResult == BTState.RUNNING) return BTState.RUNNING;

            if (conditionResult == BTState.SUCCESS)
            {
                if (Children.Count > 1 && Children[1] != null)
                    return Children[1].Run(blackBoard);
                return BTState.SUCCESS;
            }
            else
            {
                if (Children.Count > 2 && Children[2] != null)
                    return Children[2].Run(blackBoard);
                return BTState.FAILURE;
            }
        }

        public override CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode CreateRuntimeNode()
        {
            var node = new CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors.RuntimeIfThenElseNode();
            node.GUID = GUID;
            node.AbortType = (CycloneGames.BehaviorTree.Runtime.Core.RuntimeAbortType)(int)AbortType;
            foreach (var child in Children)
            {
                if (child != null) node.AddChild(child.CreateRuntimeNode());
            }
            return node;
        }
    }
}
