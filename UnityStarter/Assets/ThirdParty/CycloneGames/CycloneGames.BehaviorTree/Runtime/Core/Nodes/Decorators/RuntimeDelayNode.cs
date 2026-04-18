using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators
{
    /// <summary>
    /// Delays ticking child for DelaySeconds. Returns RUNNING during delay,
    /// then passes through child results.
    /// </summary>
    public class RuntimeDelayNode : RuntimeDecoratorNode
    {
        public float DelaySeconds { get; set; } = 1f;
        public bool UseUnscaledTime { get; set; }

        private double _startTime;
        private bool _delayComplete;

        protected override void OnStart(RuntimeBlackboard blackboard)
        {
            _startTime = RuntimeBTTime.GetTime(blackboard, UseUnscaledTime);
            _delayComplete = false;
        }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            if (!_delayComplete)
            {
                double elapsed = RuntimeBTTime.GetTime(blackboard, UseUnscaledTime) - _startTime;
                if (elapsed < DelaySeconds)
                    return RuntimeState.Running;
                _delayComplete = true;
            }

            if (Child == null) return RuntimeState.Success;
            return Child.Run(blackboard);
        }
    }
}
