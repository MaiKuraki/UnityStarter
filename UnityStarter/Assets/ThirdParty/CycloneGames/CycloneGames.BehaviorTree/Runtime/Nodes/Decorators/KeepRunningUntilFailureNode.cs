using CycloneGames.BehaviorTree.Runtime.Attributes;
using CycloneGames.BehaviorTree.Runtime.Data;
using CycloneGames.BehaviorTree.Runtime.Interfaces;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Decorators
{
    [BTInfo("Flow Control", "Keeps ticking the child as long as it returns SUCCESS. Returns FAILURE when the child fails.")]
    public class KeepRunningUntilFailureNode : DecoratorNode
    {
        protected override BTState OnRun(IBlackBoard blackBoard)
        {
            var state = Child.Run(blackBoard);
            if (state == BTState.FAILURE) return BTState.FAILURE;
            return BTState.RUNNING;
        }

        public override CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode CreateRuntimeNode()
        {
            var node = new CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators.RuntimeKeepRunningUntilFailureNode();
            node.GUID = GUID;
            if (Child != null) node.Child = Child.CreateRuntimeNode();
            return node;
        }
    }
}
