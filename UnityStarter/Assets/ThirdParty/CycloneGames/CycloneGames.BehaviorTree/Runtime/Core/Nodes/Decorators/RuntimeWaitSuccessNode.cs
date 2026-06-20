using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators
{
    public class RuntimeWaitSuccessNode : RuntimeDecoratorNode
    {
        public float WaitTime { get; set; }
        public bool UseRandomRange { get; set; }
        public float RangeMin { get; set; }
        public float RangeMax { get; set; }
        public bool UseUnscaledTime { get; set; }

        private double _startTime;
        private double _actualWaitTime;
        public float ActualWaitTime => (float)_actualWaitTime;
        public double ActualWaitTimeAsDouble => _actualWaitTime;

        protected override void OnStart(RuntimeBlackboard blackboard)
        {
            _startTime = RuntimeBTTime.GetTime(blackboard, UseUnscaledTime);
            var randomProvider = blackboard.GetService<IRuntimeBTRandomProvider>();
            _actualWaitTime = UseRandomRange
                ? (randomProvider != null ? randomProvider.Range(RangeMin, RangeMax) : Random.Range(RangeMin, RangeMax))
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
