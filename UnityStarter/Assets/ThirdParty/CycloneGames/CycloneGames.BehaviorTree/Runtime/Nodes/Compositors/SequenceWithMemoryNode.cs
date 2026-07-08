using CycloneGames.BehaviorTree.Runtime.Attributes;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Compositors
{
    [BTInfo("Sequence", "Sequence with memory: resumes from the last RUNNING child instead of restarting from the beginning.")]
    public class SequenceWithMemoryNode : CompositeNode
    {

    }
}
