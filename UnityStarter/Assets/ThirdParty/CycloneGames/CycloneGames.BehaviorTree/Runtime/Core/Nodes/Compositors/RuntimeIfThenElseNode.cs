namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors
{
    /// <summary>
    /// Expects exactly 3 children: [condition, then_branch, else_branch].
    /// Ticks condition first:
    ///   SUCCESS → tick then_branch (child[1])
    ///   FAILURE → tick else_branch (child[2])
    /// </summary>
    public class RuntimeIfThenElseNode : RuntimeCompositeNode
    {
        private int _selectedBranch; // 1 = then, 2 = else, 0 = undecided

        protected override void OnStart(RuntimeBlackboard blackboard)
        {
            _selectedBranch = 0;
            var children = Children;
            for (int i = 0; i < children.Length; i++)
                children[i].ResetState();
        }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            var children = Children;
            if (children == null || children.Length < 2) return RuntimeState.Failure;

            // Already decided: continue ticking the selected branch
            if (_selectedBranch > 0)
            {
                return children[_selectedBranch].Run(blackboard);
            }

            // Evaluate condition (child[0])
            var condState = children[0].Run(blackboard);

            if (condState == RuntimeState.Running)
                return RuntimeState.Running;

            if (condState == RuntimeState.Success)
            {
                _selectedBranch = 1;
                return children[1].Run(blackboard);
            }

            // FAILURE → else branch (if exists)
            if (children.Length >= 3)
            {
                _selectedBranch = 2;
                return children[2].Run(blackboard);
            }

            return RuntimeState.Failure;
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
