using CycloneGames.BehaviorTree.Runtime.Core.Networking;

namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors
{
    /// <summary>
    /// Executes a single randomly-selected child branch each run cycle.
    /// Re-selects on each new activation using weighted random.
    ///
    /// Supports deterministic RNG for network sync:
    ///   Set DeterministicSeedKey to a BB key hash that holds a uint seed.
    ///   When set, uses xorshift32 instead of UnityEngine.Random.
    /// </summary>
    public class RuntimeProbabilityBranch : RuntimeCompositeNode
    {
        public int SelectedBranch { get; set; }
        private float[] _weights;

        /// <summary>
        /// BB key hash holding the uint seed for deterministic randomization.
        /// 0 = use UnityEngine.Random (default, non-deterministic).
        /// </summary>
        public int DeterministicSeedKey { get; set; }

        public void SetWeights(float[] weights)
        {
            _weights = weights;
        }

        protected override void OnStart(RuntimeBlackboard blackboard)
        {
            var children = Children;
            for (int i = 0; i < children.Length; i++)
                children[i].ResetState();

            if (_weights != null && _weights.Length > 0)
            {
                float total = 0f;
                for (int i = 0; i < _weights.Length; i++)
                    total += _weights[i];

                float random;
                if (DeterministicSeedKey != 0 && blackboard.HasKey(DeterministicSeedKey))
                {
                    uint seed = (uint)blackboard.GetInt(DeterministicSeedKey);
                    var rng = new BTDeterministic.DeterministicRNG(seed);
                    random = rng.NextFloat() * total;
                    // Write back advanced state so next call produces different result
                    blackboard.SetInt(DeterministicSeedKey, (int)rng.State);
                }
                else
                {
                    random = UnityEngine.Random.Range(0f, total);
                }

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
