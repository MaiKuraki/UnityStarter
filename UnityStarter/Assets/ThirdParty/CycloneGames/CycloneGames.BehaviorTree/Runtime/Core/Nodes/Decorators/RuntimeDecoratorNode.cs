using System;

namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators
{
    public abstract class RuntimeDecoratorNode : RuntimeNode
    {
        private RuntimeNode _child;

        public RuntimeNode Child
        {
            get => _child;
            set
            {
                ThrowIfSetupFrozen();
                _child = value;
            }
        }

        public override bool CanEvaluate => Child != null && Child.CanEvaluate;

        public override bool Evaluate(RuntimeBlackboard blackboard)
        {
            if (Child == null || !Child.CanEvaluate) return true;
            return Child.Evaluate(blackboard);
        }

        public override void OnAwake()
        {
            if (Child != null)
            {
                Child.OnAwake();
            }
        }

        protected override void OnExit(
            RuntimeBlackboard blackboard,
            RuntimeNodeExitReason reason,
            Exception exception)
        {
            if (Child != null && Child.IsStarted)
            {
                Child.Abort(blackboard);
            }
        }

        protected override void OnReset(RuntimeBlackboard blackboard)
        {
            Child?.Reset(blackboard);
        }

        protected override void OnDispose(RuntimeBlackboard blackboard)
        {
            Child?.DisposeNode(blackboard);
        }
    }
}
