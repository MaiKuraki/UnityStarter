using CycloneGames.BehaviorTree.Runtime.Attributes;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Compositors
{
    [BTInfo("Conditional", "While-Do-Else: Child[0] is condition. While true, runs Child[1]. When false, runs Child[2].")]
    public class WhileDoElseNode : CompositeNode
    {
        public override CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode CreateRuntimeNode()
        {
            var node = new CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors.RuntimeWhileDoElseNode();
            node.GUID = GUID;
            AddRuntimeChildren(node);
            return node;
        }
    }
}
