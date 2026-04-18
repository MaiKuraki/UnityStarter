using CycloneGames.BehaviorTree.Runtime.Attributes;
using CycloneGames.BehaviorTree.Runtime.Data;
using CycloneGames.BehaviorTree.Runtime.Interfaces;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Decorators
{
    [BTInfo("Flow Control", "Aborts the child and returns FAILURE if it runs longer than the timeout.")]
    public class TimeoutNode : DecoratorNode
    {
        [SerializeField] private float _timeoutSeconds = 5f;
        [SerializeField] private bool _useUnscaledTime = false;
        private double _startTime;

        protected override void OnStart(IBlackBoard blackBoard)
        {
            _startTime = Core.RuntimeBTTime.GetUnityTime(_useUnscaledTime);
        }

        protected override BTState OnRun(IBlackBoard blackBoard)
        {
            double currentTime = Core.RuntimeBTTime.GetUnityTime(_useUnscaledTime);
            if (currentTime - _startTime >= _timeoutSeconds) return BTState.FAILURE;
            return Child.Run(blackBoard);
        }

        public override BTNode Clone()
        {
            var clone = (TimeoutNode)base.Clone();
            clone._timeoutSeconds = _timeoutSeconds;
            clone._useUnscaledTime = _useUnscaledTime;
            return clone;
        }

        public override CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode CreateRuntimeNode()
        {
            var node = new CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators.RuntimeTimeoutNode();
            node.GUID = GUID;
            node.TimeoutSeconds = _timeoutSeconds;
            node.UseUnscaledTime = _useUnscaledTime;
            if (Child != null) node.Child = Child.CreateRuntimeNode();
            return node;
        }
    }
}
