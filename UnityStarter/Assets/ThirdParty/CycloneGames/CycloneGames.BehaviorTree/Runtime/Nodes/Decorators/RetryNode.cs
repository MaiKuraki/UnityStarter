using CycloneGames.BehaviorTree.Runtime.Attributes;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Decorators
{
    [BTInfo("Flow Control", "Retries the child node up to MaxAttempts times upon failure.")]
    public class RetryNode : DecoratorNode
    {
        [SerializeField] private int _maxAttempts = 3;

        public int MaxAttempts => _maxAttempts;

        public override BTNode Clone()
        {
            var clone = (RetryNode)base.Clone();
            clone._maxAttempts = _maxAttempts;
            return clone;
        }

    }
}
