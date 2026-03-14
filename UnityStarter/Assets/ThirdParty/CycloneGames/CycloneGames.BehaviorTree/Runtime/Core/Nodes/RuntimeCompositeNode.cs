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
        // List used during compilation, converted to array via Seal() for 0GC iteration
        private List<RuntimeNode> _childrenList = new List<RuntimeNode>();
        private RuntimeNode[] _childrenArray;
        private bool _sealed;

        public RuntimeNode[] Children => _childrenArray;
        public int ChildCount => _childrenArray != null ? _childrenArray.Length : _childrenList.Count;

        public RuntimeAbortType AbortType { get; set; } = RuntimeAbortType.None;

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
            if (child != null)
            {
                _childrenList.Add(child);
            }
        }

        // Freeze children into a flat array, release List to reduce memory
        public void Seal()
        {
            if (_sealed) return;
            _childrenArray = _childrenList.ToArray();
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
    }
}
