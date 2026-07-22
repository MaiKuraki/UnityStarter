using System;

namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators
{
    public class RuntimeRepeatNode : RuntimeDecoratorNode
    {
        private bool _repeatForever = true;
        private int _repeatCount = 1;
        private bool _useRandomRepeatCount;
        private int _randomRangeMin = 1;
        private int _randomRangeMax = 1;

        public bool RepeatForever
        {
            get => _repeatForever;
            set => SetSetupValue(ref _repeatForever, value);
        }

        public int RepeatCount
        {
            get => _repeatCount;
            set
            {
                ThrowIfSetupFrozen();
                if (value < 1)
                {
                    throw new ArgumentOutOfRangeException(nameof(RepeatCount), value, "Repeat count must be at least 1.");
                }
                _repeatCount = value;
            }
        }

        public bool UseRandomRepeatCount
        {
            get => _useRandomRepeatCount;
            set => SetSetupValue(ref _useRandomRepeatCount, value);
        }

        public int RandomRangeMin
        {
            get => _randomRangeMin;
            set
            {
                ThrowIfSetupFrozen();
                if (value < 1 || value == int.MaxValue)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(RandomRangeMin),
                        value,
                        "Repeat range minimum must be from 1 through int.MaxValue - 1.");
                }
                _randomRangeMin = value;
            }
        }

        public int RandomRangeMax
        {
            get => _randomRangeMax;
            set
            {
                ThrowIfSetupFrozen();
                if (value < 1 || value == int.MaxValue)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(RandomRangeMax),
                        value,
                        "Repeat range maximum must be from 1 through int.MaxValue - 1.");
                }
                _randomRangeMax = value;
            }
        }

        private int _currentRepeatCount;
        private int _targetRepeatCount;
        public int CurrentRepeatCount => _currentRepeatCount; // Exposed for debug

        protected override void ValidateSetup()
        {
            if (!RepeatForever && UseRandomRepeatCount && RandomRangeMax < RandomRangeMin)
            {
                throw new InvalidOperationException("Repeat range must be ordered min <= max.");
            }
        }

        protected override void OnStart(RuntimeBlackboard blackboard)
        {
            _currentRepeatCount = 0;
            _targetRepeatCount = !RepeatForever && UseRandomRepeatCount
                ? RuntimeRandomUtility.RangeInt(blackboard, RandomRangeMin, RandomRangeMax + 1)
                : RepeatCount;
        }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            if (Child == null) return RuntimeState.Success;

            var state = Child.Run(blackboard);

            if (state == RuntimeState.Success || state == RuntimeState.Failure)
            {
                if (RepeatForever)
                {
                    IncrementRepeatCount();
                    return RuntimeState.Running;
                }
                else
                {
                    IncrementRepeatCount();
                    if (_currentRepeatCount < _targetRepeatCount)
                    {
                        return RuntimeState.Running;
                    }
                    else
                    {
                        return RuntimeState.Success;
                    }
                }
            }

            return RuntimeState.Running;
        }

        private void IncrementRepeatCount()
        {
            if (_currentRepeatCount < int.MaxValue)
            {
                _currentRepeatCount++;
            }
        }
    }
}
