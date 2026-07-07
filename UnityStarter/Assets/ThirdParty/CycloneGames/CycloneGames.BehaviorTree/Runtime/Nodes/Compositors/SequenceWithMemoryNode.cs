using CycloneGames.BehaviorTree.Runtime.Attributes;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Compositors
{
    [BTInfo("Sequence", "Sequence with memory: resumes from the last RUNNING child instead of restarting from the beginning.")]
    public class SequenceWithMemoryNode : CompositeNode
    {
        public override CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode CreateRuntimeNode()
        {
            var node = new CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors.RuntimeSequenceWithMemory();
            node.GUID = GUID;
            AddRuntimeChildren(node);
            return node;
        }
    }
}
