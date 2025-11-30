using CycloneGames.BehaviorTree.Runtime.Data;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Decorators
{
    public class CoolDownNode : DecoratorNode
    {
        [SerializeField] private float _coolDown = 1f;
        [SerializeField] private bool _resetOnSuccess = false;
        private float _lastTime = 0f;
        private bool _isCoolDownStarted = false;
        public override BTState Evaluate(BlackBoard blackBoard)
        {
            if (Time.time - _lastTime > _coolDown)
            {
                return Child.Evaluate(blackBoard);
            }
            return BTState.FAILURE;
        }

        protected override BTState OnRun(BlackBoard blackBoard)
        {
            if (Time.time - _lastTime > _coolDown)
            {
                _isCoolDownStarted = true;
                return Child.Run(blackBoard);
            }
            return BTState.FAILURE;
        }
        protected override void OnStop(BlackBoard blackBoard)
        {
            if (!_isCoolDownStarted)
            {
                return;
            }
            _isCoolDownStarted = false;
            base.OnStop(blackBoard);
            if (!_resetOnSuccess)
            {
                _lastTime = Time.time;
                return;
            }
            if (Child.State == BTState.SUCCESS)
            {
                _lastTime = Time.time;
            }
        }

        public override BTNode Clone()
        {
            var clone = (CoolDownNode)base.Clone();
            clone._coolDown = _coolDown;
            return clone;
        }
    }
}