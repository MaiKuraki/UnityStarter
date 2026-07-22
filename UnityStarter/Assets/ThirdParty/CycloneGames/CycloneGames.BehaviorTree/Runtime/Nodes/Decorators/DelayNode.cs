using CycloneGames.BehaviorTree.Runtime.Attributes;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Decorators
{
    [BTInfo("Flow Control", "Waits for a specified delay before executing the child node.")]
    public class DelayNode : DecoratorNode
    {
        [SerializeField] private float _delaySeconds = 1f;
        [SerializeField] private bool _useUnscaledTime = false;

        public float DelaySeconds => _delaySeconds;
        public bool UseUnscaledTime => _useUnscaledTime;

        public override BTNode Clone()
        {
            var clone = (DelayNode)base.Clone();
            clone._delaySeconds = _delaySeconds;
            clone._useUnscaledTime = _useUnscaledTime;
            return clone;
        }

    }
}
