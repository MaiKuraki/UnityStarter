namespace CycloneGames.BehaviorTree.Runtime.Nodes.Compositors
{
    public class SelectorNode : CompositeNode
    {
        public override CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode CreateRuntimeNode()
        {
            var node = new CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors.RuntimeSelector();
            node.GUID = GUID;
            AddRuntimeChildren(node);
            return node;
        }
    }
}
