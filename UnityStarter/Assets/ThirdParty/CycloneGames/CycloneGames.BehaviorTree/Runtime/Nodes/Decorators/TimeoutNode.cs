using CycloneGames.BehaviorTree.Runtime.Attributes;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Decorators
{
    [BTInfo("Flow Control", "Aborts the child and returns FAILURE if it runs longer than the timeout.")]
    public class TimeoutNode : DecoratorNode
    {
        [SerializeField] private float _timeoutSeconds = 5f;
        [SerializeField] private bool _useUnscaledTime = false;

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
            SetRuntimeChild(node);
            return node;
        }
    }
}
