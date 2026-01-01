using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Actions
{
    public class RuntimeWaitNode : RuntimeNode
    {
        public float Duration { get; set; }
        private float _startTime;
        public float StartTime => _startTime;

        protected override void OnStart(RuntimeBlackboard blackboard)
        {
#if UNITY_5_3_OR_NEWER
            _startTime = Time.time;
#else
            // Fallback for pure C# (using simple system ticks converted to seconds, conceptual)
            _startTime = (float)(System.DateTime.Now.Ticks / 10000000.0);
#endif
        }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
#if UNITY_5_3_OR_NEWER
            float currentTime = Time.time;
#else
            float currentTime = (float)(System.DateTime.Now.Ticks / 10000000.0);
#endif

            if (currentTime - _startTime >= Duration)
            {
                return RuntimeState.Success;
            }

            return RuntimeState.Running;
        }
    }
}
