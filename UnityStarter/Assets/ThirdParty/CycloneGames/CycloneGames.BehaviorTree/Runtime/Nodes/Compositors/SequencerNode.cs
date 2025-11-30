using CycloneGames.BehaviorTree.Runtime.Data;
using CycloneGames.BehaviorTree.Runtime.Interfaces;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Compositors
{
    public class SequencerNode : CompositeNode
    {
        private int _current = 0;

        protected override BTState OnActiveEvaluate(IBlackBoard blackBoard)
        {
            if (Children.Count == 0) return BTState.SUCCESS;

            int maxIndex = _current < Children.Count ? _current : Children.Count;
            for (int i = 0; i < maxIndex; i++)
            {
                var child = Children[i];
                if (child == null) continue;
                if (!child.CanReEvaluate) continue;
                if (child.Evaluate(blackBoard) == BTState.FAILURE) return BTState.FAILURE;
            }
            return BTState.SUCCESS;
        }

        protected override BTState OnLowerPriorityEvaluate(IBlackBoard blackBoard)
        {
            if (Children.Count == 0) return BTState.SUCCESS;

            int maxIndex = (_current + 1) < Children.Count ? (_current + 1) : Children.Count;
            for (int i = 0; i < maxIndex; i++)
            {
                var child = Children[i];
                if (child == null) continue;
                if (!child.CanReEvaluate) continue;
                if (!child.EnableHijack) continue;
                if (child.Evaluate(blackBoard) == BTState.FAILURE) return BTState.FAILURE;
            }
            return BTState.SUCCESS;
        }

        protected override void OnStart(IBlackBoard blackBoard)
        {
            _current = 0;
        }

        protected override BTState RunChildren(IBlackBoard blackBoard)
        {
            if (Children.Count == 0) return BTState.SUCCESS;
            if (_current < 0 || _current >= Children.Count)
            {
                _current = 0;
            }

            var child = Children[_current];
            if (child == null)
            {
                _current++;
                return _current >= Children.Count ? BTState.SUCCESS : BTState.RUNNING;
            }

            switch (child.Run(blackBoard))
            {
                case BTState.FAILURE:
                    _current++;
                    return BTState.FAILURE;
                case BTState.RUNNING:
                    return BTState.RUNNING;
                case BTState.SUCCESS:
                    _current++;
                    break;
            }
            return _current >= Children.Count ? BTState.SUCCESS : BTState.RUNNING;
        }
    }
}