using System;
using CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators;

namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors
{
    /// <summary>
    /// Unreal-style Service node: attaches to a composite and runs a periodic
    /// side-effect callback while the composite is active.
    ///
    /// Usage pattern:
    ///   var service = new RuntimeServiceNode();
    ///   service.Interval = 0.5f; // run every 0.5 seconds
    ///   service.OnServiceTick = (bb) => {
    ///       // Update perception, target selection, etc.
    ///       var perception = bb.GetContextOwner<AIPerceptionComponent>();
    ///       var target = perception?.GetClosestSightTarget();
    ///       if (target != null)
    ///           bb.SetObject(Animator.StringToHash("Target"), target);
    ///   };
    ///
    /// Open for extension: subclass and override OnServiceUpdate for custom logic,
    /// or use the delegate-based OnServiceTick for lightweight setup.
    /// </summary>
    public class RuntimeServiceNode : RuntimeDecoratorNode
    {
        /// <summary>
        /// Interval in seconds between service ticks. 0 = every frame.
        /// </summary>
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

        /// <summary>
        /// Optional random deviation added to interval to stagger service updates.
        /// Actual interval = Interval + Random(-RandomDeviation, +RandomDeviation)
        /// </summary>
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

        /// <summary>
        /// Whether to use unscaled time (UI/pause-friendly).
        /// </summary>
        public bool UseUnscaledTime
        {
            get => _useUnscaledTime;
            set => SetSetupValue(ref _useUnscaledTime, value);
        }

        /// <summary>
        /// Delegate-based service callback. Set this for lightweight service logic.
        /// If null, override OnServiceUpdate instead.
        /// </summary>
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
            // Run service immediately on start
            RunService(blackboard);
        }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            if (Child == null) return RuntimeState.Failure;

            // Check service interval
            double now = RuntimeBTTime.GetTime(blackboard, UseUnscaledTime);
            if (now - _lastServiceTime >= _currentInterval)
            {
                _lastServiceTime = now;
                _currentInterval = ComputeInterval(blackboard);
                RunService(blackboard);
            }

            return Child.Run(blackboard);
        }

        /// <summary>
        /// Override for custom service logic in subclasses.
        /// Called at Interval rate while the parent composite is active.
        /// </summary>
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
