using System;

namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Conditions
{
    public sealed class RuntimeRandomChanceNode : RuntimeNode
    {
        private float _chance;
        private float _outOf;
        private uint _seed;
        private RuntimeDeterministicRandom _deterministicRandom;

        public RuntimeRandomChanceNode(float chance = 1f, float outOf = 1f, uint seed = 0u)
        {
            ValidateRange(chance, outOf);
            _chance = chance;
            _outOf = outOf;
            Seed = seed;
        }

        public float Chance
        {
            get => _chance;
            set
            {
                ThrowIfSetupFrozen();
                ValidateRange(value, _outOf);
                _chance = value;
            }
        }

        public float OutOf
        {
            get => _outOf;
            set
            {
                ThrowIfSetupFrozen();
                ValidateRange(_chance, value);
                _outOf = value;
            }
        }

        public uint Seed
        {
            get => _seed;
            set
            {
                ThrowIfSetupFrozen();
                _seed = value;
                _deterministicRandom = new RuntimeDeterministicRandom(value);
            }
        }

        private string _name;
        public string Name
        {
            get => _name;
            set => SetSetupValue(ref _name, value);
        }

        public override bool CanEvaluate => true;

        public override bool Evaluate(RuntimeBlackboard blackboard)
        {
            if (_chance <= 0f)
            {
                return false;
            }

            if (_chance >= _outOf)
            {
                return true;
            }

            float value = _seed != 0u
                ? _deterministicRandom.NextFloat()
                : RuntimeRandomUtility.Range(blackboard, 0f, 1f);
            return value < _chance / _outOf;
        }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            return Evaluate(blackboard) ? RuntimeState.Success : RuntimeState.Failure;
        }

        protected override void OnReset(RuntimeBlackboard blackboard)
        {
            _deterministicRandom = new RuntimeDeterministicRandom(_seed);
        }

        private static void ValidateRange(float chance, float outOf)
        {
            if (float.IsNaN(outOf) || float.IsInfinity(outOf) || outOf <= 0f)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(outOf),
                    outOf,
                    "The denominator must be finite and greater than zero.");
            }

            if (float.IsNaN(chance) || float.IsInfinity(chance) || chance < 0f || chance > outOf)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(chance),
                    chance,
                    "Chance must be finite and between zero and the denominator.");
            }
        }
    }
}
