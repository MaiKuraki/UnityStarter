using System;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators
{
    public class RuntimeWaitSuccessNode : RuntimeDecoratorNode
    {
        private float _waitTime;
        private bool _useRandomRange;
        private float _rangeMin;
        private float _rangeMax;
        private bool _useUnscaledTime;

        public float WaitTime
        {
            get => _waitTime;
            set
            {
                ThrowIfSetupFrozen();
                ValidateFiniteNonNegativeSetupValue(value, nameof(WaitTime));
                _waitTime = value;
            }
        }

        public bool UseRandomRange
        {
            get => _useRandomRange;
            set => SetSetupValue(ref _useRandomRange, value);
        }

        public float RangeMin
        {
            get => _rangeMin;
            set
            {
                ThrowIfSetupFrozen();
                ValidateFiniteNonNegativeSetupValue(value, nameof(RangeMin));
                _rangeMin = value;
            }
        }

        public float RangeMax
        {
            get => _rangeMax;
            set
            {
                ThrowIfSetupFrozen();
                ValidateFiniteNonNegativeSetupValue(value, nameof(RangeMax));
                _rangeMax = value;
            }
        }

        public bool UseUnscaledTime
        {
            get => _useUnscaledTime;
            set => SetSetupValue(ref _useUnscaledTime, value);
        }

        private double _startTime;
        private double _actualWaitTime;
        public float ActualWaitTime => (float)_actualWaitTime;
        public double ActualWaitTimeAsDouble => _actualWaitTime;

        protected override void ValidateSetup()
        {
            if (UseRandomRange && RangeMax < RangeMin)
            {
                throw new InvalidOperationException("WaitSuccess range must be ordered min <= max.");
            }
        }

        protected override void OnStart(RuntimeBlackboard blackboard)
        {
            _startTime = RuntimeBTTime.GetTime(blackboard, UseUnscaledTime);
            var randomProvider = blackboard.GetService<IRuntimeBTRandomProvider>();
            _actualWaitTime = UseRandomRange
                ? (randomProvider != null
                    ? randomProvider.Range(RangeMin, RangeMax)
                    : UnityEngine.Random.Range(RangeMin, RangeMax))
                : WaitTime;
        }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            if (Child == null) return RuntimeState.Success;

            double elapsed = RuntimeBTTime.GetTime(blackboard, UseUnscaledTime) - _startTime;
            if (elapsed >= _actualWaitTime)
            {
                return RuntimeState.Failure;
            }

            var result = Child.Run(blackboard);
            return result == RuntimeState.Success ? RuntimeState.Success : RuntimeState.Running;
        }
    }
}
