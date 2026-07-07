using CycloneGames.BehaviorTree.Runtime.Attributes;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Decorators
{
    [BTInfo("Flow Control", "Executes the child only once. Subsequent ticks return the cached result.")]
    public class RunOnceNode : DecoratorNode
    {
        public override CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode CreateRuntimeNode()
        {
            var node = new CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators.RuntimeRunOnceNode();
            node.GUID = GUID;
            SetRuntimeChild(node);
            return node;
        }
    }
}
