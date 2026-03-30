using CycloneGames.BehaviorTree.Runtime.Attributes;
using CycloneGames.BehaviorTree.Runtime.Data;
using CycloneGames.BehaviorTree.Runtime.Interfaces;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Compositors
{
    [BTInfo("Sequence", "Sequence with memory: resumes from the last RUNNING child instead of restarting from the beginning.")]
    public class SequenceWithMemoryNode : CompositeNode
    {
        private int _current;

        protected override BTState OnActiveEvaluate(IBlackBoard blackBoard)
        {
            for (int i = 0; i < Children.Count; i++)
            {
                var child = Children[i];
                if (child == null) continue;
                if (!child.CanReEvaluate) continue;
                if (child.Evaluate(blackBoard) == BTState.FAILURE) return BTState.FAILURE;
            }
            return BTState.SUCCESS;
        }

        protected override void OnStart(IBlackBoard blackBoard)
        {
            base.OnStart(blackBoard);
            _current = 0;
        }

        protected override BTState RunChildren(IBlackBoard blackBoard)
        {
            while (_current < Children.Count)
            {
                var state = Children[_current].Run(blackBoard);
                if (state == BTState.FAILURE) return BTState.FAILURE;
                if (state == BTState.RUNNING) return BTState.RUNNING;
                _current++;
            }
            return BTState.SUCCESS;
        }

        public override CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode CreateRuntimeNode()
        {
            var node = new CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors.RuntimeSequenceWithMemory();
            node.GUID = GUID;
            node.AbortType = (CycloneGames.BehaviorTree.Runtime.Core.RuntimeAbortType)(int)AbortType;
            foreach (var child in Children)
            {
                if (child != null) node.AddChild(child.CreateRuntimeNode());
            }
            return node;
        }
    }
}
