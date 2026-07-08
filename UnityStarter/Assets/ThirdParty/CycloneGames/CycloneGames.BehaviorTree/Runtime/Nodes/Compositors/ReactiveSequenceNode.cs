using CycloneGames.BehaviorTree.Runtime.Attributes;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Compositors
{
    [BTInfo("Reactive", "Sequence that re-evaluates all children from the first every tick. Fails immediately if any child fails.")]
    public class ReactiveSequenceNode : CompositeNode
    {

    }
}
