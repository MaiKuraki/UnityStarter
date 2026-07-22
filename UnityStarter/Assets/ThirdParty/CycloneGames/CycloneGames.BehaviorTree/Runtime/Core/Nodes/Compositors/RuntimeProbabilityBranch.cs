using System;

namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors
{
    /// <summary>
    /// Selects one child per activation using validated non-negative weights.
    /// An empty weight set assigns equal probability to every child.
    /// </summary>
    public class RuntimeProbabilityBranch : RuntimeCompositeNode
    {
        private float[] _weights = Array.Empty<float>();
        private float _totalWeight;
        private int _deterministicSeedKey;

        public int SelectedBranch { get; private set; } = -1;

        /// <summary>
        /// Blackboard key containing the deterministic random state. Zero uses the configured random provider.
        /// </summary>
        public int DeterministicSeedKey
        {
            get => _deterministicSeedKey;
            set
            {
                ThrowIfSetupFrozen();
                _deterministicSeedKey = value;
            }
        }

        public void SetWeights(float[] weights)
        {
            ThrowIfSetupFrozen();
            if (IsStarted)
            {
                throw new InvalidOperationException("Probability weights cannot change during an active execution.");
            }

            if (weights == null || weights.Length == 0)
            {
                _weights = Array.Empty<float>();
                _totalWeight = 0f;
                return;
            }

            var copy = new float[weights.Length];
            double total = 0d;
            for (int i = 0; i < weights.Length; i++)
            {
                float weight = weights[i];
                if (float.IsNaN(weight) || float.IsInfinity(weight) || weight < 0f)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(weights),
                        weight,
                        $"Weight at index {i} must be finite and non-negative.");
                }

                copy[i] = weight;
                total += weight;
            }

            if (double.IsInfinity(total) || total > float.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(weights), "The total weight exceeds the supported range.");
            }

            _weights = copy;
            _totalWeight = (float)total;
        }

        public override void OnAwake()
        {
            base.OnAwake();
            ValidateConfiguration();
        }

        protected override void ValidateSetup()
        {
            ValidateConfiguration();
        }

        private void ValidateConfiguration()
        {
            if (_weights.Length != 0 && _weights.Length != ChildCount)
            {
                throw new InvalidOperationException(
                    $"Probability weight count ({_weights.Length}) must match child count ({ChildCount}).");
            }

            if (_weights.Length != 0 && _totalWeight <= 0f)
            {
                throw new InvalidOperationException("At least one probability weight must be greater than zero.");
            }
        }

        protected override void OnStart(RuntimeBlackboard blackboard)
        {
            ValidateConfiguration();
            RuntimeNode[] children = ChildArray;
            SelectedBranch = -1;
            for (int i = 0; i < children.Length; i++)
            {
                children[i].PrepareForActivation();
            }

            if (children.Length == 0)
            {
                return;
            }

            if (_weights.Length == 0)
            {
                SelectedBranch = SelectUniformBranch(blackboard, children.Length);
                return;
            }

            float sample = NextSample(blackboard, _totalWeight);
            if (float.IsNaN(sample) || float.IsInfinity(sample))
            {
                throw new InvalidOperationException("The random provider returned a non-finite probability sample.");
            }

            if (sample < 0f)
            {
                sample = 0f;
            }
            else if (sample >= _totalWeight)
            {
                sample = PreviousRepresentablePositive(_totalWeight);
            }

            float cumulative = 0f;
            int lastPositive = -1;
            for (int i = 0; i < _weights.Length; i++)
            {
                float weight = _weights[i];
                if (weight <= 0f)
                {
                    continue;
                }

                lastPositive = i;
                cumulative += weight;
                if (sample < cumulative)
                {
                    SelectedBranch = i;
                    return;
                }
            }

            SelectedBranch = lastPositive;
        }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            RuntimeNode[] children = ChildArray;
            if (children == null || SelectedBranch < 0 || SelectedBranch >= children.Length)
            {
                return RuntimeState.Failure;
            }

            return children[SelectedBranch].Run(blackboard);
        }

        protected override void OnExit(
            RuntimeBlackboard blackboard,
            RuntimeNodeExitReason reason,
            Exception exception)
        {
            base.OnExit(blackboard, reason, exception);
            if (reason != RuntimeNodeExitReason.Completed)
            {
                SelectedBranch = -1;
            }
        }

        protected override void OnReset(RuntimeBlackboard blackboard)
        {
            base.OnReset(blackboard);
            SelectedBranch = -1;
        }

        private int SelectUniformBranch(RuntimeBlackboard blackboard, int childCount)
        {
            if (DeterministicSeedKey == 0)
            {
                return RuntimeRandomUtility.RangeInt(blackboard, 0, childCount);
            }

            uint state = unchecked((uint)blackboard.GetInt(DeterministicSeedKey, 1));
            var random = RuntimeDeterministicRandom.FromState(state);
            int selected = random.NextInt(childCount);
            blackboard.SetInt(DeterministicSeedKey, unchecked((int)random.State));
            return selected;
        }

        private float NextSample(RuntimeBlackboard blackboard, float maxExclusive)
        {
            if (DeterministicSeedKey == 0)
            {
                return RuntimeRandomUtility.Range(blackboard, 0f, maxExclusive);
            }

            uint state = unchecked((uint)blackboard.GetInt(DeterministicSeedKey, 1));
            var random = RuntimeDeterministicRandom.FromState(state);
            float sample = random.Range(0f, maxExclusive);
            blackboard.SetInt(DeterministicSeedKey, unchecked((int)random.State));
            return sample;
        }

        private static float PreviousRepresentablePositive(float value)
        {
            if (value <= 0f)
            {
                return 0f;
            }

            int bits = BitConverter.SingleToInt32Bits(value);
            return BitConverter.Int32BitsToSingle(bits - 1);
        }
    }
}
