using CycloneGames.BehaviorTree.Runtime.Attributes;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Compositors
{
    [BTInfo("Reactive", "Fallback that re-evaluates all children from the first every tick. Succeeds immediately if any child succeeds.")]
    public class ReactiveFallbackNode : CompositeNode
    {
        public override CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode CreateRuntimeNode()
        {
            var node = new CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors.RuntimeReactiveFallback();
            node.GUID = GUID;
            AddRuntimeChildren(node);
            return node;
        }
    }
}
