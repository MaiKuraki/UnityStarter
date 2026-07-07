using CycloneGames.BehaviorTree.Runtime.Attributes;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Compositors
{
    [BTInfo("Reactive", "Sequence that re-evaluates all children from the first every tick. Fails immediately if any child fails.")]
    public class ReactiveSequenceNode : CompositeNode
    {
        public override CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode CreateRuntimeNode()
        {
            var node = new CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors.RuntimeReactiveSequence();
            node.GUID = GUID;
            AddRuntimeChildren(node);
            return node;
        }
    }
}
