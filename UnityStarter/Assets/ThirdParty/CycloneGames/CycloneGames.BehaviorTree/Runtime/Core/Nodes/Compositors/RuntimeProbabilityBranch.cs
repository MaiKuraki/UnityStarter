namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors
{
    /// <summary>
    /// Executes a single randomly-selected child branch each run cycle.
    /// Re-selects on each new activation using weighted random.
    /// </summary>
    public class RuntimeProbabilityBranch : RuntimeCompositeNode
    {
        public int SelectedBranch { get; set; }
        private float[] _weights;

        public void SetWeights(float[] weights)
        {
            _weights = weights;
        }

        protected override void OnStart(RuntimeBlackboard blackboard)
        {
            if (_weights != null && _weights.Length > 0)
            {
                float total = 0f;
                for (int i = 0; i < _weights.Length; i++)
                    total += _weights[i];

                float random = UnityEngine.Random.Range(0f, total);
                float cumulative = 0f;
                for (int i = 0; i < _weights.Length; i++)
                {
                    cumulative += _weights[i];
                    if (random <= cumulative)
                    {
                        SelectedBranch = i;
                        break;
                    }
                }
            }
        }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            var children = Children;
            if (children == null || children.Length == 0 || SelectedBranch < 0 || SelectedBranch >= children.Length)
                return RuntimeState.Failure;

            return children[SelectedBranch].Run(blackboard);
        }

        protected override void OnStop(RuntimeBlackboard blackboard)
        {
            var children = Children;
            if (children != null && SelectedBranch >= 0 && SelectedBranch < children.Length)
            {
                if (children[SelectedBranch].IsStarted)
                {
                    children[SelectedBranch].Abort(blackboard);
                }
            }
        }
    }
}
