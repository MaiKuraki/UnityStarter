using System;
using System.Collections;
using System.Collections.Generic;

namespace CycloneGames.BehaviorTree.Runtime.Core
{
    public enum RuntimeAbortType
    {
        None,
        Self,
        LowerPriority,
        Both
    }

    public abstract class RuntimeCompositeNode : RuntimeNode
    {
        // Setup uses a list. Seal converts it once to the array used by every runtime hot path.
        private List<RuntimeNode> _childrenList = new List<RuntimeNode>();
        private RuntimeNode[] _childrenArray;
        private readonly ReadOnlyChildrenView _childrenView;
        private bool _sealed;
        private RuntimeAbortType _abortType;

        protected RuntimeCompositeNode()
        {
            _childrenView = new ReadOnlyChildrenView(this);
        }

        /// <summary>
        /// Read-only setup/debug view. Runtime execution uses the sealed array directly.
        /// </summary>
        public IReadOnlyList<RuntimeNode> Children => _childrenView;
        public int ChildCount => _childrenArray != null ? _childrenArray.Length : _childrenList.Count;

        public RuntimeAbortType AbortType
        {
            get => _abortType;
            set
            {
                ThrowIfSetupFrozen();
                if ((uint)(int)value > (uint)RuntimeAbortType.Both)
                {
                    throw new ArgumentOutOfRangeException(nameof(AbortType), value, "Unsupported abort mode.");
                }
                _abortType = value;
            }
        }

        internal bool IsSealed => _sealed;

        /// <summary>
        /// Allocation-free internal traversal buffer. The graph must be sealed first.
        /// </summary>
        internal RuntimeNode[] ChildArray
        {
            get
            {
                if (!_sealed)
                {
                    throw new InvalidOperationException(
                        $"{GetType().FullName} must be sealed before runtime traversal.");
                }

                return _childrenArray;
            }
        }

        /// <summary>
        /// Current child index for composites that execute sequentially.
        /// Override in subclasses (Sequencer, Selector, etc.) for network snapshot support.
        /// </summary>
        public virtual int CurrentIndex => 0;

        public override bool CanEvaluate
        {
            get
            {
                var children = _childrenArray;
                if (children == null) return false;
                for (int i = 0; i < children.Length; i++)
                {
                    if (children[i].CanEvaluate) return true;
                }
                return false;
            }
        }

        public override bool Evaluate(RuntimeBlackboard blackboard)
        {
            var children = _childrenArray;
            if (children == null) return true;
            for (int i = 0; i < children.Length; i++)
            {
                if (!children[i].CanEvaluate) continue;
                if (!children[i].Evaluate(blackboard)) return false;
            }
            return true;
        }

        public void AddChild(RuntimeNode child)
        {
            if (_sealed)
            {
                throw new InvalidOperationException(
                    $"{GetType().FullName} children cannot be changed after the composite is sealed.");
            }

            ThrowIfSetupFrozen();
            if (child == null)
            {
                throw new ArgumentNullException(nameof(child));
            }

            _childrenList.Add(child);
        }

        /// <summary>
        /// Freezes child topology into a compact array and releases setup storage.
        /// </summary>
        public void Seal()
        {
            ThrowIfSetupFrozen();
            if (_sealed)
            {
                return;
            }

            _childrenArray = _childrenList.Count == 0
                ? Array.Empty<RuntimeNode>()
                : _childrenList.ToArray();
            _childrenList = null;
            _sealed = true;
        }

        public override void OnAwake()
        {
            if (!_sealed) Seal();

            for (int i = 0; i < _childrenArray.Length; i++)
            {
                _childrenArray[i].OnAwake();
            }
        }

        protected override void OnExit(
            RuntimeBlackboard blackboard,
            RuntimeNodeExitReason reason,
            Exception exception)
        {
            AbortRunningChildren(blackboard);
        }

        protected override void OnReset(RuntimeBlackboard blackboard)
        {
            RuntimeNode[] children = _childrenArray;
            if (children == null)
            {
                return;
            }

            for (int i = 0; i < children.Length; i++)
            {
                children[i].Reset(blackboard);
            }
        }

        protected override void OnDispose(RuntimeBlackboard blackboard)
        {
            RuntimeNode[] children = _childrenArray;
            if (children == null)
            {
                return;
            }

            System.Exception pendingException = null;
            for (int i = 0; i < children.Length; i++)
            {
                try
                {
                    children[i].DisposeNode(blackboard);
                }
                catch (System.Exception exception)
                {
                    pendingException = pendingException == null
                        ? exception
                        : new System.AggregateException(pendingException, exception);
                }
            }

            if (pendingException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(pendingException).Throw();
            }
        }

        protected void AbortRunningChildren(RuntimeBlackboard blackboard)
        {
            RuntimeNode[] children = _childrenArray;
            if (children == null)
            {
                return;
            }

            System.Exception pendingException = null;
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].IsStarted)
                {
                    try
                    {
                        children[i].Abort(blackboard);
                    }
                    catch (System.Exception exception)
                    {
                        pendingException = pendingException == null
                            ? exception
                            : new System.AggregateException(pendingException, exception);
                    }
                }
            }

            if (pendingException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(pendingException).Throw();
            }
        }

        private sealed class ReadOnlyChildrenView : IReadOnlyList<RuntimeNode>
        {
            private readonly RuntimeCompositeNode _owner;

            internal ReadOnlyChildrenView(RuntimeCompositeNode owner)
            {
                _owner = owner;
            }

            public int Count => _owner.ChildCount;

            public RuntimeNode this[int index]
            {
                get
                {
                    RuntimeNode[] children = _owner._childrenArray;
                    return children != null
                        ? children[index]
                        : _owner._childrenList[index];
                }
            }

            public IEnumerator<RuntimeNode> GetEnumerator()
            {
                int count = Count;
                for (int i = 0; i < count; i++)
                {
                    yield return this[i];
                }
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }
    }
}
