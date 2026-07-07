using CycloneGames.BehaviorTree.Runtime.Attributes;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Decorators
{
    [BTInfo("Flow Control", "Keeps ticking the child as long as it returns SUCCESS. Returns FAILURE when the child fails.")]
    public class KeepRunningUntilFailureNode : DecoratorNode
    {
        public override CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode CreateRuntimeNode()
        {
            var node = new CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators.RuntimeKeepRunningUntilFailureNode();
            node.GUID = GUID;
            SetRuntimeChild(node);
            return node;
        }
    }
}
