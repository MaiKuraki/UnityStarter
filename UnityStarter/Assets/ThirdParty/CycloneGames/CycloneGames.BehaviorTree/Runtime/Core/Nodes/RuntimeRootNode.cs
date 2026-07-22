using System;

namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes
{
    public class RuntimeRootNode : RuntimeNode
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

        public override void OnAwake()
        {
            if (Child != null)
            {
                Child.OnAwake();
            }
        }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            if (Child == null) return RuntimeState.Failure;
            return Child.Run(blackboard);
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
