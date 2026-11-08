using System;
using CycloneGames.BehaviorTree.Core.Nodes.Decorators;

namespace CycloneGames.BehaviorTree.Core.Nodes.Compositors
{
    public class RuntimeServiceNode : RuntimeDecoratorNode
    {
        public float Interval
        {
            get => _interval;
            set
            {
                ThrowIfSetupFrozen();
                ValidateFiniteNonNegative(value, nameof(Interval));
                _interval = value;
            }
        }

        public float RandomDeviation
        {
            get => _randomDeviation;
            set
            {
                ThrowIfSetupFrozen();
                ValidateFiniteNonNegative(value, nameof(RandomDeviation));
                _randomDeviation = value;
            }
        }

        public bool UseUnscaledTime
        {
            get => _useUnscaledTime;
            set => SetSetupValue(ref _useUnscaledTime, value);
        }

        public System.Action<RuntimeBlackboard> OnServiceTick
        {
            get => _onServiceTick;
            set => SetSetupValue(ref _onServiceTick, value);
        }

        private float _interval = 0.5f;
        private float _randomDeviation;
        private bool _useUnscaledTime;
        private System.Action<RuntimeBlackboard> _onServiceTick;
        private double _lastServiceTime;
        private double _currentInterval;

        protected override void ValidateSetup()
        {
            if (RandomDeviation > float.MaxValue * 0.5f
                || Interval > float.MaxValue - RandomDeviation)
            {
                throw new InvalidOperationException(
                    "Service interval and random deviation exceed the finite sampling range.");
            }
        }

        protected override void OnStart(RuntimeBlackboard blackboard)
        {
            _lastServiceTime = RuntimeBTTime.GetTime(blackboard, UseUnscaledTime);
            _currentInterval = ComputeInterval(blackboard);
            RunService(blackboard);
        }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            if (Child == null) return RuntimeState.Failure;

            double now = RuntimeBTTime.GetTime(blackboard, UseUnscaledTime);
            if (now - _lastServiceTime >= _currentInterval)
            {
                _lastServiceTime = now;
                _currentInterval = ComputeInterval(blackboard);
                RunService(blackboard);
            }

            return Child.Run(blackboard);
        }

        protected virtual void OnServiceUpdate(RuntimeBlackboard blackboard) { }

        private void RunService(RuntimeBlackboard blackboard)
        {
            OnServiceTick?.Invoke(blackboard);
            OnServiceUpdate(blackboard);
        }

        private double ComputeInterval(RuntimeBlackboard blackboard)
        {
            if (RandomDeviation <= 0f) return Interval;
            float deviation = RuntimeRandomUtility.Range(
                blackboard,
                -RandomDeviation,
                RandomDeviation);
            float result = Interval + deviation;
            return result > 0f ? result : 0f;
        }

        private static void ValidateFiniteNonNegative(float value, string propertyName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
            {
                throw new ArgumentOutOfRangeException(
                    propertyName,
                    value,
                    "Value must be finite and non-negative.");
            }
        }
    }
}
