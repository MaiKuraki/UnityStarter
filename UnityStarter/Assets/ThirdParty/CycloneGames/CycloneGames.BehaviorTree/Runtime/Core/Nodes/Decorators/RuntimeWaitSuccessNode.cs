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

        private float _timer;
        private float _actualWaitTime;
        public float ActualWaitTime => _actualWaitTime;

        protected override void OnStart(RuntimeBlackboard blackboard)
        {
            _timer = 0f;
            _actualWaitTime = UseRandomRange ? Random.Range(RangeMin, RangeMax) : WaitTime;
        }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            if (Child == null) return RuntimeState.Success;

            _timer += UseUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            if (_timer >= _actualWaitTime)
            {
                return RuntimeState.Failure;
            }

            var result = Child.Run(blackboard);
            return result == RuntimeState.Success ? RuntimeState.Success : RuntimeState.Running;
        }
    }
}