namespace CycloneGames.BehaviorTree.Runtime.Nodes.Decorators
{
    public class SucceederNode : DecoratorNode
    {
        public override CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode CreateRuntimeNode()
        {
            var node = new CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators.RuntimeSucceederNode();
            node.GUID = GUID;
            SetRuntimeChild(node);
            return node;
        }
    }
}
