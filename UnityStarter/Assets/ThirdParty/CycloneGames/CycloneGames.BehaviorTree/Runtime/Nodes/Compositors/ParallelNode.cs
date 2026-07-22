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

        public int ModeValue => (int)_mode;
    }
}
