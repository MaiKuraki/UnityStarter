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
    ///       var perception = bb.GetContextOwner&lt;AIPerceptionComponent&gt;();
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
        public float Interval { get; set; } = 0.5f;

        /// <summary>
        /// Optional random deviation added to interval to stagger service updates.
        /// Actual interval = Interval + Random(-RandomDeviation, +RandomDeviation)
        /// </summary>
        public float RandomDeviation { get; set; } = 0f;

        /// <summary>
        /// Whether to use unscaled time (UI/pause-friendly).
        /// </summary>
        public bool UseUnscaledTime { get; set; } = false;

        /// <summary>
        /// Delegate-based service callback. Set this for lightweight service logic.
        /// If null, override OnServiceUpdate instead.
        /// </summary>
        public System.Action<RuntimeBlackboard> OnServiceTick { get; set; }

        private float _lastServiceTime;
        private float _currentInterval;

        protected override void OnStart(RuntimeBlackboard blackboard)
        {
            _lastServiceTime = GetTime();
            _currentInterval = ComputeInterval();
            // Run service immediately on start
            RunService(blackboard);
        }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            if (Child == null) return RuntimeState.Failure;

            // Check service interval
            float now = GetTime();
            if (now - _lastServiceTime >= _currentInterval)
            {
                _lastServiceTime = now;
                _currentInterval = ComputeInterval();
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

        private float GetTime()
        {
            return UseUnscaledTime ? UnityEngine.Time.unscaledTime : UnityEngine.Time.time;
        }

        private float ComputeInterval()
        {
            if (RandomDeviation <= 0f) return Interval;
            float deviation = UnityEngine.Random.Range(-RandomDeviation, RandomDeviation);
            float result = Interval + deviation;
            return result > 0f ? result : 0f;
        }
    }
}
