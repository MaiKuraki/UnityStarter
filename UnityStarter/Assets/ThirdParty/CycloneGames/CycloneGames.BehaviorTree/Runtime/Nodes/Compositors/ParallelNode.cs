using CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Compositors
{
    public class ParallelNode : CompositeNode
    {
        private enum ParallelMode
        {
            Default,
            UntilAnyComplete,
            UntilAnyFailure,
            UntilAnySuccess,
        }

        [SerializeField] private ParallelMode _mode = ParallelMode.Default;

        public override CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode CreateRuntimeNode()
        {
            var node = new RuntimeParallelNode();
            node.GUID = GUID;
            node.Mode = (RuntimeParallelMode)_mode;
            AddRuntimeChildren(node);
            return node;
        }
    }
}
