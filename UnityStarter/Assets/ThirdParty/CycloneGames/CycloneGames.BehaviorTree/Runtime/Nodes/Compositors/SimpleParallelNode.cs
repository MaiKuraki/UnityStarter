using CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Compositors
{
    public class SimpleParallelNode : CompositeNode
    {
        public override CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode CreateRuntimeNode()
        {
            var node = new RuntimeParallelNode();
            node.GUID = GUID;
            node.Mode = RuntimeParallelMode.Default;
            AddRuntimeChildren(node);
            return node;
        }
    }
}
