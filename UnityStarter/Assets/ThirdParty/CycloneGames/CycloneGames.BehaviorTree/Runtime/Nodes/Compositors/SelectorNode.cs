using CycloneGames.BehaviorTree.Runtime.Data;
using CycloneGames.BehaviorTree.Runtime.Interfaces;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Compositors
{
    public class SelectorNode : CompositeNode
    {
        private int _current = 0;

        protected override BTState OnActiveEvaluate(IBlackBoard blackBoard)
        {
            if (Children.Count == 0) return BTState.FAILURE;

            for (int i = 0; i < Children.Count; i++)
            {
                var child = Children[i];
                if (child == null) continue;
                if (!child.CanReEvaluate)
                {
                    return BTState.SUCCESS;
                }
                if (child.Evaluate(blackBoard) == BTState.SUCCESS) return BTState.SUCCESS;
            }
            return BTState.FAILURE;
        }

        protected override BTState OnDeActiveEvaluate(IBlackBoard blackBoard)
        {
            if (Children.Count == 0) return BTState.FAILURE;

            for (int i = 0; i < Children.Count; i++)
            {
                var child = Children[i];
                if (child == null) continue;
                if (!child.CanReEvaluate)
                {
                    return BTState.SUCCESS;
                }
                if (child.Evaluate(blackBoard) == BTState.SUCCESS) return BTState.SUCCESS;
            }
            return BTState.FAILURE;
        }

        protected override BTState OnLowerPriorityEvaluate(IBlackBoard blackBoard)
        {
            HandleLowerPriority(blackBoard);
            return BTState.SUCCESS;
        }

        private void HandleLowerPriority(IBlackBoard blackBoard)
        {
            if (Children.Count == 0 || _current < 0 || _current >= Children.Count) return;

            var currentChild = Children[_current];
            if (currentChild == null) return;

            for (int i = 0; i < _current && i < Children.Count; i++)
            {
                var child = Children[i];
                if (child == null) continue;
                if (!child.CanReEvaluate) continue;
                if (!child.EnableHijack) continue;
                if (child.Evaluate(blackBoard) == BTState.FAILURE) continue;

                currentChild.BTStop(blackBoard);
                _current = i;
                return;
            }
        }

        protected override void OnStart(IBlackBoard blackBoard)
        {
            base.OnStart(blackBoard);
            _current = 0;
        }

        protected override BTState RunChildren(IBlackBoard blackBoard)
        {
            if (Children.Count == 0) return BTState.FAILURE;
            if (_current < 0 || _current >= Children.Count)
            {
                _current = 0;
            }

            var child = Children[_current];
            if (child == null)
            {
                _current++;
                return _current >= Children.Count ? BTState.FAILURE : BTState.RUNNING;
            }

            switch (child.Run(blackBoard))
            {
                case BTState.RUNNING:
                    return BTState.RUNNING;
                case BTState.SUCCESS:
                    _current++;
                    return BTState.SUCCESS;
                case BTState.FAILURE:
                    _current++;
                    break;
            }
            return _current >= Children.Count ? BTState.FAILURE : BTState.RUNNING;
        }

        public override CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode CreateRuntimeNode()
        {
            var runtimeNode = new CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors.RuntimeSelector();
            runtimeNode.GUID = GUID;
            foreach (var child in Children)
            {
                if (child != null)
                {
                    runtimeNode.AddChild(child.CreateRuntimeNode());
                }
            }
            return runtimeNode;
        }
    }
}