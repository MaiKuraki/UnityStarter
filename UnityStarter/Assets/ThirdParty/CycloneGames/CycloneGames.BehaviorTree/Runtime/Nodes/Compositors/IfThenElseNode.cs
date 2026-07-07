using CycloneGames.BehaviorTree.Runtime.Attributes;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Compositors
{
    [BTInfo("Conditional", "If-Then-Else: Child[0] is condition, Child[1] is 'then', Child[2] is 'else'.")]
    public class IfThenElseNode : CompositeNode
    {
        public override CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode CreateRuntimeNode()
        {
            var node = new CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors.RuntimeIfThenElseNode();
            node.GUID = GUID;
            AddRuntimeChildren(node);
            return node;
        }
    }
}
