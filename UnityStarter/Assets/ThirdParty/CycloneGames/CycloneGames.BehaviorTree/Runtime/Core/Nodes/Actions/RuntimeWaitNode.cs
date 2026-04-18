using UnityEngine;
using CycloneGames.BehaviorTree.Runtime.Core;

namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Actions
{
    public class RuntimeWaitNode : RuntimeNode
    {
        public float Duration { get; set; }
        public bool UseUnscaledTime { get; set; }
        public bool UseRandomRange { get; set; }
        public float RangeMin { get; set; }
        public float RangeMax { get; set; }

        private double _startTime;
        private double _actualDuration;
        public float StartTime => (float)_startTime;
        public double StartTimeAsDouble => _startTime;
        public float ActualDuration => (float)_actualDuration;
        public double ActualDurationAsDouble => _actualDuration;

        protected override void OnStart(RuntimeBlackboard blackboard)
        {
            var randomProvider = blackboard.GetService<IRuntimeBTRandomProvider>();
            _actualDuration = UseRandomRange
                ? (randomProvider != null ? randomProvider.Range(RangeMin, RangeMax) : Random.Range(RangeMin, RangeMax))
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
