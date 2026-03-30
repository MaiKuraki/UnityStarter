namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors
{
    /// <summary>
    /// N-way switch. Reads an int value from the blackboard and ticks the
    /// matching child index. If no match, ticks the last child (default).
    /// 0GC: uses int key hash for blackboard lookup.
    /// </summary>
    public class RuntimeSwitchNode : RuntimeCompositeNode
    {
        public int VariableKeyHash { get; set; }

        protected override void OnStart(RuntimeBlackboard blackboard)
        {
            var children = Children;
            for (int i = 0; i < children.Length; i++)
                children[i].ResetState();
        }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            var children = Children;
            if (children == null || children.Length == 0) return RuntimeState.Failure;

            int caseValue = blackboard.GetInt(VariableKeyHash, -1);
            int targetIndex;

            if (caseValue >= 0 && caseValue < children.Length - 1)
            {
                targetIndex = caseValue;
            }
            else
            {
                // Default: last child
                targetIndex = children.Length - 1;
            }

            return children[targetIndex].Run(blackboard);
        }

        protected override void OnStop(RuntimeBlackboard blackboard)
        {
            var children = Children;
            if (children == null) return;
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].IsStarted) children[i].Abort(blackboard);
            }
        }
    }
}
