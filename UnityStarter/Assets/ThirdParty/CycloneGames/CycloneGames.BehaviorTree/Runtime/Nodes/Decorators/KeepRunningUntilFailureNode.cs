using CycloneGames.BehaviorTree.Runtime.Attributes;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Decorators
{
    [BTInfo("Flow Control", "Keeps ticking the child as long as it returns SUCCESS. Returns FAILURE when the child fails.")]
    public class KeepRunningUntilFailureNode : DecoratorNode
    {

    }
}
