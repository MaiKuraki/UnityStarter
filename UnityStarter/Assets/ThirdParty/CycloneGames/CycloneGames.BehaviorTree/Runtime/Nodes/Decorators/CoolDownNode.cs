using CycloneGames.BehaviorTree.Runtime.Data;
using CycloneGames.BehaviorTree.Runtime.Interfaces;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Decorators
{
    public class CoolDownNode : DecoratorNode
    {
        [SerializeField] private float _coolDown = 1f;
        [SerializeField] private bool _resetOnSuccess = false;
        private double _lastTime = 0d;
        private bool _isCoolDownStarted = false;
        public override BTState Evaluate(IBlackBoard blackBoard)
        {
            if (Core.RuntimeBTTime.GetUnityTime(false) - _lastTime > _coolDown)
            {
                return Child.Evaluate(blackBoard);
            }
            return BTState.FAILURE;
        }

        protected override BTState OnRun(IBlackBoard blackBoard)
        {
            if (Core.RuntimeBTTime.GetUnityTime(false) - _lastTime > _coolDown)
            {
                _isCoolDownStarted = true;
                return Child.Run(blackBoard);
            }
            return BTState.FAILURE;
        }
        protected override void OnStop(IBlackBoard blackBoard)
        {
            if (!_isCoolDownStarted)
            {
                return;
            }
            _isCoolDownStarted = false;
            base.OnStop(blackBoard);
            if (!_resetOnSuccess)
            {
                _lastTime = Core.RuntimeBTTime.GetUnityTime(false);
                return;
            }
            if (Child.State == BTState.SUCCESS)
            {
                _lastTime = Core.RuntimeBTTime.GetUnityTime(false);
            }
        }

        public override BTNode Clone()
        {
            var clone = (CoolDownNode)base.Clone();
            clone._coolDown = _coolDown;
            return clone;
        }

        public override CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode CreateRuntimeNode()
        {
            var node = new CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators.RuntimeCoolDownNode();
            node.GUID = GUID;
            node.CoolDown = _coolDown;
            node.ResetOnSuccess = _resetOnSuccess;
            if (Child != null)
            {
                node.Child = Child.CreateRuntimeNode();
            }
            return node;
        }
    }
}
