using CycloneGames.BehaviorTree.Runtime.Attributes;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Decorators
{
    [BTInfo("Flow Control", "Executes the child only once. Subsequent ticks return the cached result.")]
    public class RunOnceNode : DecoratorNode
    {

    }
}
