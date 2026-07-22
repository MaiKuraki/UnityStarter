using System;

namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators
{
    /// <summary>
    /// Executes its child against an isolated blackboard that falls back to the parent blackboard.
    /// The scoped blackboard is owned by this node and is disposed with the runtime tree.
    /// </summary>
    public class RuntimeBlackboardNode : RuntimeDecoratorNode
    {
        private RuntimeBlackboard _scopedBlackboard;

        public override void OnAwake()
        {
            if (_scopedBlackboard != null)
            {
                throw new InvalidOperationException("The scoped blackboard node was awakened more than once.");
            }

            _scopedBlackboard = new RuntimeBlackboard();
            base.OnAwake();
        }

        protected override void OnStart(RuntimeBlackboard blackboard)
        {
            BindScope(blackboard);
        }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            if (Child == null)
            {
                return RuntimeState.Success;
            }

            return Child.Run(_scopedBlackboard);
        }

        protected override void OnExit(
            RuntimeBlackboard blackboard,
            RuntimeNodeExitReason reason,
            Exception exception)
        {
            try
            {
                if (Child != null && Child.IsStarted)
                {
                    Child.Abort(_scopedBlackboard);
                }
            }
            finally
            {
                ReleaseScope();
            }
        }

        protected override void OnReset(RuntimeBlackboard blackboard)
        {
            BindScope(blackboard);
            try
            {
                Child?.Reset(_scopedBlackboard);
            }
            finally
            {
                ReleaseScope();
            }
        }

        protected override void OnDispose(RuntimeBlackboard blackboard)
        {
            RuntimeBlackboard scopedBlackboard = _scopedBlackboard;
            _scopedBlackboard = null;
            if (scopedBlackboard == null)
            {
                Child?.DisposeNode(blackboard);
                return;
            }

            try
            {
                Child?.DisposeNode(scopedBlackboard);
            }
            finally
            {
                try
                {
                    scopedBlackboard.Parent = null;
                }
                finally
                {
                    try
                    {
                        scopedBlackboard.Context = null;
                    }
                    finally
                    {
                        scopedBlackboard.Dispose();
                    }
                }
            }
        }

        private void BindScope(RuntimeBlackboard blackboard)
        {
            if (_scopedBlackboard == null)
            {
                throw new InvalidOperationException("The scoped blackboard has not been initialized.");
            }

            _scopedBlackboard.Parent = blackboard;
            _scopedBlackboard.Context = blackboard?.Context;
            if (blackboard != null)
            {
                _scopedBlackboard.StringHashFunc = blackboard.StringHashFunc;
            }
        }

        private void ReleaseScope()
        {
            if (_scopedBlackboard == null)
            {
                return;
            }

            try
            {
                _scopedBlackboard.Clear();
            }
            finally
            {
                try
                {
                    _scopedBlackboard.Parent = null;
                }
                finally
                {
                    _scopedBlackboard.Context = null;
                }
            }
        }
    }
}
