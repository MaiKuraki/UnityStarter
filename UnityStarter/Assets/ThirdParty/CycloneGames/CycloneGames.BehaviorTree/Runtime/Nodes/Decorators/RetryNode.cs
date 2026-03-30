using CycloneGames.BehaviorTree.Runtime.Attributes;
using CycloneGames.BehaviorTree.Runtime.Data;
using CycloneGames.BehaviorTree.Runtime.Interfaces;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Decorators
{
    [BTInfo("Flow Control", "Retries the child node up to MaxAttempts times upon failure.")]
    public class RetryNode : DecoratorNode
    {
        [SerializeField] private int _maxAttempts = 3;
        private int _currentAttempt;

        protected override void OnStart(IBlackBoard blackBoard)
        {
            _currentAttempt = 0;
        }

        protected override BTState OnRun(IBlackBoard blackBoard)
        {
            var state = Child.Run(blackBoard);
            if (state == BTState.FAILURE)
            {
                _currentAttempt++;
                if (_currentAttempt >= _maxAttempts) return BTState.FAILURE;
                return BTState.RUNNING;
            }
            if (state == BTState.SUCCESS) return BTState.SUCCESS;
            return BTState.RUNNING;
        }

        public override BTNode Clone()
        {
            var clone = (RetryNode)base.Clone();
            clone._maxAttempts = _maxAttempts;
            return clone;
        }

        public override CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode CreateRuntimeNode()
        {
            var node = new CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators.RuntimeRetryNode();
            node.GUID = GUID;
            node.MaxAttempts = _maxAttempts;
            if (Child != null) node.Child = Child.CreateRuntimeNode();
            return node;
        }
    }
}
