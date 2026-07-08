using CycloneGames.BehaviorTree.Runtime.Attributes;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Decorators
{
    [BTInfo("Flow Control", "Always returns FAILURE regardless of the child's result.")]
    public class ForceFailureNode : DecoratorNode
    {

    }
}
