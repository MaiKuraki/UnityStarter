namespace CycloneGames.BehaviorTree.Runtime.Nodes.Decorators
{
    public class InvertNode : DecoratorNode
    {
        public override CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode CreateRuntimeNode()
        {
            var node = new CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators.RuntimeInverterNode();
            node.GUID = GUID;
            SetRuntimeChild(node);
            return node;
        }
    }
}
