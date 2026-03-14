using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Actions
{
    public class RuntimeWaitNode : RuntimeNode
    {
        public float Duration { get; set; }
        public bool UseUnscaledTime { get; set; }
        public bool UseRandomRange { get; set; }
        public float RangeMin { get; set; }
        public float RangeMax { get; set; }

        private float _startTime;
        private float _actualDuration;
        public float StartTime => _startTime;
        public float ActualDuration => _actualDuration;

        protected override void OnStart(RuntimeBlackboard blackboard)
        {
            _actualDuration = UseRandomRange ? Random.Range(RangeMin, RangeMax) : Duration;
#if UNITY_5_3_OR_NEWER
            _startTime = UseUnscaledTime ? Time.unscaledTime : Time.time;
#else
            _startTime = (float)(System.DateTime.Now.Ticks / 10000000.0);
#endif
        }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
#if UNITY_5_3_OR_NEWER
            float currentTime = UseUnscaledTime ? Time.unscaledTime : Time.time;
#else
            float currentTime = (float)(System.DateTime.Now.Ticks / 10000000.0);
#endif

            if (currentTime - _startTime >= _actualDuration)
            {
                return RuntimeState.Success;
            }

            return RuntimeState.Running;
        }
    }
}
