using CycloneGames.BehaviorTree.Runtime.Data;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Compositors
{
    public class ParallelNode : CompositeNode
    {
        private enum ParallelMode
        {
            Default,
            UntilAnyComplete,
            UntilAnyFailure,
            UntilAnySuccess,
        }
        [SerializeField] private ParallelMode _mode = ParallelMode.Default;
        protected override BTState OnActiveEvaluate(BlackBoard blackBoard)
        {
            for (int i = 0; i < Children.Count; i++)
            {
                if (!Children[i].CanReEvaluate) continue;
                var result = Children[i].Evaluate(blackBoard);
                if (result == BTState.FAILURE) return BTState.FAILURE;
            }
            return BTState.SUCCESS;
        }

        protected override BTState RunChildren(BlackBoard blackBoard)
        {
            switch (_mode)
            {
                case ParallelMode.Default:
                    return RunDefault(blackBoard);
                case ParallelMode.UntilAnyComplete:
                    return RunUntilAnyComplete(blackBoard);
                case ParallelMode.UntilAnyFailure:
                    return RunUntilAnyFailure(blackBoard);
                case ParallelMode.UntilAnySuccess:
                    return RunUntilAnySuccess(blackBoard);
                default:
                    return BTState.FAILURE;
            }
        }
        private BTState RunDefault(BlackBoard blackBoard)
        {
            for (int i = 0; i < Children.Count; i++)
            {
                Children[i].Run(blackBoard);
            }
            return BTState.RUNNING;
        }
        private BTState RunUntilAnyComplete(BlackBoard blackBoard)
        {
            for (int i = 0; i < Children.Count; i++)
            {
                var state = Children[i].Run(blackBoard);
                bool isComplete = state == BTState.SUCCESS || state == BTState.FAILURE;
                if (isComplete) return BTState.SUCCESS;
            }
            return BTState.RUNNING;
        }
        private BTState RunUntilAnyFailure(BlackBoard blackBoard)
        {
            for (int i = 0; i < Children.Count; i++)
            {
                var state = Children[i].Run(blackBoard);
                if (state == BTState.FAILURE) return BTState.SUCCESS;
            }
            return BTState.RUNNING;
        }
        private BTState RunUntilAnySuccess(BlackBoard blackBoard)
        {
            for (int i = 0; i < Children.Count; i++)
            {
                var state = Children[i].Run(blackBoard);
                if (state == BTState.SUCCESS) return BTState.SUCCESS;
            }
            return BTState.RUNNING;
        }
    }
}