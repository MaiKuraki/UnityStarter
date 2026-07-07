namespace CycloneGames.BehaviorTree.Runtime.Nodes.Compositors
{
    public class SequencerNode : CompositeNode
    {
        public override CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode CreateRuntimeNode()
        {
            var node = new CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors.RuntimeSequencer();
            node.GUID = GUID;
            AddRuntimeChildren(node);
            return node;
        }
    }
}
