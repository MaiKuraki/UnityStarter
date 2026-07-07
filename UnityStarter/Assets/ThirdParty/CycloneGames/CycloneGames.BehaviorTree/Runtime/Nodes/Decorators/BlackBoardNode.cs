using CycloneGames.BehaviorTree.Runtime.Attributes;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Decorators
{
    [BTInfo("BlackBoard", "Decorator that provides a blackboard context for child nodes.")]
    public class BlackBoardNode : DecoratorNode
    {
        public override CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode CreateRuntimeNode()
        {
            var node = new CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators.RuntimeBlackboardNode();
            node.GUID = GUID;
            SetRuntimeChild(node);
            return node;
        }
    }
}
