using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators
{
    public class RuntimeCoolDownNode : RuntimeDecoratorNode
    {
        public float CoolDown { get; set; }
        public bool ResetOnSuccess { get; set; }

        private double _lastTime;
        private bool _isCoolDownStarted;

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            if (RuntimeBTTime.GetTime(blackboard, false) - _lastTime > CoolDown)
            {
                if (Child == null) return RuntimeState.Failure;
                _isCoolDownStarted = true;
                return Child.Run(blackboard);
            }
            return RuntimeState.Failure;
        }

        protected override void OnStop(RuntimeBlackboard blackboard)
        {
            if (!_isCoolDownStarted) return;
            _isCoolDownStarted = false;

            if (Child != null && Child.IsStarted)
            {
                Child.Abort(blackboard);
            }

            if (!ResetOnSuccess || (Child != null && Child.State == RuntimeState.Success))
            {
                _lastTime = RuntimeBTTime.GetTime(blackboard, false);
            }
        }
    }
}
