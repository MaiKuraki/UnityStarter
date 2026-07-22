using System;
using UnityEngine;
using CycloneGames.BehaviorTree.Runtime.Core;

namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Actions
{
    public class RuntimeWaitNode : RuntimeNode
    {
        private float _duration;
        private bool _useUnscaledTime;
        private bool _useRandomRange;
        private float _rangeMin;
        private float _rangeMax;

        public float Duration
        {
            get => _duration;
            set
            {
                ThrowIfSetupFrozen();
                ValidateFiniteNonNegativeSetupValue(value, nameof(Duration));
                _duration = value;
            }
        }

        public bool UseUnscaledTime
        {
            get => _useUnscaledTime;
            set => SetSetupValue(ref _useUnscaledTime, value);
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

        private double _startTime;
        private double _actualDuration;
        public float StartTime => (float)_startTime;
        public double StartTimeAsDouble => _startTime;
        public float ActualDuration => (float)_actualDuration;
        public double ActualDurationAsDouble => _actualDuration;

        protected override void ValidateSetup()
        {
            if (UseRandomRange && RangeMax < RangeMin)
            {
                throw new InvalidOperationException("Wait range must be ordered min <= max.");
            }
        }

        protected override void OnStart(RuntimeBlackboard blackboard)
        {
            var randomProvider = blackboard.GetService<IRuntimeBTRandomProvider>();
            _actualDuration = UseRandomRange
                ? (randomProvider != null
                    ? randomProvider.Range(RangeMin, RangeMax)
                    : UnityEngine.Random.Range(RangeMin, RangeMax))
                : Duration;

            _startTime = GetCurrentTime(blackboard);
        }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            double currentTime = GetCurrentTime(blackboard);

            if (currentTime - _startTime >= _actualDuration)
            {
                return RuntimeState.Success;
            }

            return RuntimeState.Running;
        }

        private double GetCurrentTime(RuntimeBlackboard blackboard)
        {
            return RuntimeBTTime.GetTime(blackboard, UseUnscaledTime);
        }
    }
}
