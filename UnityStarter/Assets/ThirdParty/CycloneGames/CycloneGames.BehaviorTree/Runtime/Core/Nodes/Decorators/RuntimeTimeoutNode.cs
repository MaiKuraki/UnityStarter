using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators
{
    /// <summary>
    /// Halts child and returns FAILURE if child runs longer than TimeoutSeconds.
    /// Supports both scaled and unscaled time.
    /// </summary>
    public class RuntimeTimeoutNode : RuntimeDecoratorNode
    {
        private float _timeoutSeconds = 5f;
        private bool _useUnscaledTime;

        public float TimeoutSeconds
        {
            get => _timeoutSeconds;
            set
            {
                ThrowIfSetupFrozen();
                ValidateFiniteNonNegativeSetupValue(value, nameof(TimeoutSeconds));
                _timeoutSeconds = value;
            }
        }

        public bool UseUnscaledTime
        {
            get => _useUnscaledTime;
            set => SetSetupValue(ref _useUnscaledTime, value);
        }

        private double _startTime;

        protected override void OnStart(RuntimeBlackboard blackboard)
        {
            _startTime = RuntimeBTTime.GetTime(blackboard, UseUnscaledTime);
        }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            if (Child == null) return RuntimeState.Failure;

            double elapsed = RuntimeBTTime.GetTime(blackboard, UseUnscaledTime) - _startTime;
            if (elapsed >= TimeoutSeconds)
            {
                if (Child.IsStarted) Child.Abort(blackboard);
                return RuntimeState.Failure;
            }

            return Child.Run(blackboard);
        }
    }
}
