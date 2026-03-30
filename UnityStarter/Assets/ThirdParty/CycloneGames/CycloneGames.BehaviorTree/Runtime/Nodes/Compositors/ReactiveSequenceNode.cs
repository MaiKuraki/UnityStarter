using CycloneGames.BehaviorTree.Runtime.Attributes;
using CycloneGames.BehaviorTree.Runtime.Data;
using CycloneGames.BehaviorTree.Runtime.Interfaces;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Compositors
{
    [BTInfo("Reactive", "Sequence that re-evaluates all children from the first every tick. Fails immediately if any child fails.")]
    public class ReactiveSequenceNode : CompositeNode
    {
        protected override BTState OnActiveEvaluate(IBlackBoard blackBoard)
        {
            for (int i = 0; i < Children.Count; i++)
            {
                var child = Children[i];
                if (child == null) continue;
                if (!child.CanReEvaluate) continue;
                if (child.Evaluate(blackBoard) == BTState.FAILURE) return BTState.FAILURE;
            }
            return BTState.SUCCESS;
        }

        protected override BTState RunChildren(IBlackBoard blackBoard)
        {
            for (int i = 0; i < Children.Count; i++)
            {
                var state = Children[i].Run(blackBoard);
                if (state == BTState.FAILURE) return BTState.FAILURE;
                if (state == BTState.RUNNING) return BTState.RUNNING;
            }
            return BTState.SUCCESS;
        }

        public override CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode CreateRuntimeNode()
        {
            var node = new CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors.RuntimeReactiveSequence();
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
