using CycloneGames.BehaviorTree.Runtime.Attributes;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Decorators
{
    [BTInfo("Flow Control", "Aborts the child and returns FAILURE if it runs longer than the timeout.")]
    public class TimeoutNode : DecoratorNode
    {
        [SerializeField] private float _timeoutSeconds = 5f;
        [SerializeField] private bool _useUnscaledTime = false;

        public float TimeoutSeconds => _timeoutSeconds;
        public bool UseUnscaledTime => _useUnscaledTime;

        public override BTNode Clone()
        {
            var clone = (TimeoutNode)base.Clone();
            clone._timeoutSeconds = _timeoutSeconds;
            clone._useUnscaledTime = _useUnscaledTime;
            return clone;
        }

    }
}
