using CycloneGames.BehaviorTree.Runtime.Attributes;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Decorators
{
    [BTInfo("Flow Control", "Always returns FAILURE regardless of the child's result.")]
    public class ForceFailureNode : DecoratorNode
    {
        public override CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode CreateRuntimeNode()
        {
            var node = new CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators.RuntimeForceFailureNode();
            node.GUID = GUID;
            SetRuntimeChild(node);
            return node;
        }
    }
}
