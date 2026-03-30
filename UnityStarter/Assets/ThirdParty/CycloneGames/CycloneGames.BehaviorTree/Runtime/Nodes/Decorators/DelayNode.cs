using CycloneGames.BehaviorTree.Runtime.Attributes;
using CycloneGames.BehaviorTree.Runtime.Data;
using CycloneGames.BehaviorTree.Runtime.Interfaces;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Decorators
{
    [BTInfo("Flow Control", "Waits for a specified delay before executing the child node.")]
    public class DelayNode : DecoratorNode
    {
        [SerializeField] private float _delaySeconds = 1f;
        [SerializeField] private bool _useUnscaledTime = false;
        private float _startTime;
        private bool _delayCompleted;

        protected override void OnStart(IBlackBoard blackBoard)
        {
            _startTime = _useUnscaledTime ? Time.unscaledTime : Time.time;
            _delayCompleted = false;
        }

        protected override BTState OnRun(IBlackBoard blackBoard)
        {
            if (!_delayCompleted)
            {
                float currentTime = _useUnscaledTime ? Time.unscaledTime : Time.time;
                if (currentTime - _startTime < _delaySeconds) return BTState.RUNNING;
                _delayCompleted = true;
            }
            return Child.Run(blackBoard);
        }

        public override BTNode Clone()
        {
            var clone = (DelayNode)base.Clone();
            clone._delaySeconds = _delaySeconds;
            clone._useUnscaledTime = _useUnscaledTime;
            return clone;
        }

        public override CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode CreateRuntimeNode()
        {
            var node = new CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators.RuntimeDelayNode();
            node.GUID = GUID;
            node.DelaySeconds = _delaySeconds;
            node.UseUnscaledTime = _useUnscaledTime;
            if (Child != null) node.Child = Child.CreateRuntimeNode();
            return node;
        }
    }
}
