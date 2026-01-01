using CycloneGames.BehaviorTree.Runtime.Data;
using CycloneGames.BehaviorTree.Runtime.Interfaces;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Decorators
{
    public class SucceederNode : DecoratorNode
    {
        protected override BTState OnRun(IBlackBoard blackBoard)
        {
            if (Child.Run(blackBoard) == BTState.FAILURE)
            {
                return BTState.SUCCESS;
            }
            return Child.State;
        }

        public override CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode CreateRuntimeNode()
        {
            var node = new CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators.RuntimeSucceederNode();
            node.GUID = GUID;
            if (Child != null)
            {
                node.Child = Child.CreateRuntimeNode();
            }
            return node;
        }
    }
}