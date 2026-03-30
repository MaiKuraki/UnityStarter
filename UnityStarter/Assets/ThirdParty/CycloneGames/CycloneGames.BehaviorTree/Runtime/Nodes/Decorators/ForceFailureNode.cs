using CycloneGames.BehaviorTree.Runtime.Attributes;
using CycloneGames.BehaviorTree.Runtime.Data;
using CycloneGames.BehaviorTree.Runtime.Interfaces;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Decorators
{
    [BTInfo("Flow Control", "Always returns FAILURE regardless of the child's result.")]
    public class ForceFailureNode : DecoratorNode
    {
        protected override BTState OnRun(IBlackBoard blackBoard)
        {
            var state = Child.Run(blackBoard);
            if (state == BTState.RUNNING) return BTState.RUNNING;
            return BTState.FAILURE;
        }

        public override CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode CreateRuntimeNode()
        {
            var node = new CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators.RuntimeForceFailureNode();
            node.GUID = GUID;
            if (Child != null) node.Child = Child.CreateRuntimeNode();
            return node;
        }
    }
}
