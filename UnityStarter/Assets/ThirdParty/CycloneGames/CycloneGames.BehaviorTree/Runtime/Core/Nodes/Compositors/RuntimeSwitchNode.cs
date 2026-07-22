namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors
{
    /// <summary>
    /// Reads an integer case from the blackboard. The final child is the default branch.
    /// Changing the selected case aborts the previously running branch before entering the new one.
    /// </summary>
    public class RuntimeSwitchNode : RuntimeCompositeNode
    {
        private int _activeIndex = -1;
        private int _variableKeyHash;

        public int VariableKeyHash
        {
            get => _variableKeyHash;
            set => SetSetupValue(ref _variableKeyHash, value);
        }

        public override int CurrentIndex => _activeIndex;

        protected override void OnStart(RuntimeBlackboard blackboard)
        {
            _activeIndex = -1;
            RuntimeNode[] children = ChildArray;
            for (int i = 0; i < children.Length; i++)
            {
                children[i].PrepareForActivation();
            }
        }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            RuntimeNode[] children = ChildArray;
            if (children == null || children.Length == 0)
            {
                return RuntimeState.Failure;
            }

            int caseValue = blackboard.GetInt(VariableKeyHash, -1);
            int targetIndex = caseValue >= 0 && caseValue < children.Length - 1
                ? caseValue
                : children.Length - 1;

            if (_activeIndex != targetIndex)
            {
                if (_activeIndex >= 0 &&
                    _activeIndex < children.Length &&
                    children[_activeIndex].IsStarted)
                {
                    children[_activeIndex].Abort(blackboard);
                }

                _activeIndex = targetIndex;
            }

            return children[_activeIndex].Run(blackboard);
        }

        protected override void OnExit(
            RuntimeBlackboard blackboard,
            RuntimeNodeExitReason reason,
            System.Exception exception)
        {
            base.OnExit(blackboard, reason, exception);
            if (reason != RuntimeNodeExitReason.Completed)
            {
                _activeIndex = -1;
            }
        }

        protected override void OnReset(RuntimeBlackboard blackboard)
        {
            base.OnReset(blackboard);
            _activeIndex = -1;
        }
    }
}
