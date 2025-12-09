using System.Collections.Generic;
using CycloneGames.BehaviorTree.Runtime.Data;
using CycloneGames.BehaviorTree.Runtime.Interfaces;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Compositors
{
    public abstract class CompositeNode : BTNode
    {
        public override bool CanReEvaluate
        {
            get
            {
                for (int i = 0; i < Children.Count; i++)
                {
                    if (Children[i] != null && Children[i].CanReEvaluate)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        public override bool EnableHijack
        {
            get
            {
                switch (AbortType)
                {
                    case ConditionalAbortType.NONE:
                    case ConditionalAbortType.SELF:
                    default:
                        return false;
                    case ConditionalAbortType.LOWER_PRIORITY:
                    case ConditionalAbortType.BOTH:
                        return true;
                }
            }
        }

        [HideInInspector] public List<BTNode> Children = new List<BTNode>();
        public ConditionalAbortType AbortType = ConditionalAbortType.NONE;

        private static readonly NodePositionComparer _positionComparer = new NodePositionComparer();

        public override void Inject(object container)
        {
            base.Inject(container);
            for (int i = 0; i < Children.Count; i++)
            {
                Children[i]?.Inject(container);
            }
        }

        public override BTNode Clone()
        {
            var clone = (CompositeNode)base.Clone();

            if (Application.isPlaying)
            {
                clone.Children = new List<BTNode>(Children.Count);
                for (int i = 0; i < Children.Count; i++)
                {
                    clone.Children.Add(Children[i].Clone());
                }
            }
            else
            {
                clone.Children = new List<BTNode>();
            }

            return clone;
        }

        /// <summary>
        /// Resets all children nodes to NOT_ENTERED state when the composite node restarts.
        /// Ensures clean state for nodes like RepeatNode that restart their parent.
        /// </summary>
        /// <param name="blackBoard">The blackboard instance</param>
        protected override void OnStart(IBlackBoard blackBoard)
        {
            for (int i = 0; i < Children.Count; i++)
            {
                var child = Children[i];
                if (child != null)
                {
                    if (child.IsStarted)
                    {
                        child.BTStop(blackBoard);
                    }
                    child.State = BTState.NOT_ENTERED;
                }
            }
        }

        protected override BTState OnRun(IBlackBoard blackBoard)
        {
            if (AbortType is ConditionalAbortType.BOTH or ConditionalAbortType.SELF)
            {
                var evaluate = Evaluate(blackBoard);
                if (evaluate == BTState.FAILURE) return BTState.FAILURE;
            }
            if (OnLowerPriorityEvaluate(blackBoard) == BTState.FAILURE)
            {
                return BTState.FAILURE;
            }
            return RunChildren(blackBoard);
        }

        public override BTState Evaluate(IBlackBoard blackBoard)
        {
            return IsStarted ? OnActiveEvaluate(blackBoard) : OnDeActiveEvaluate(blackBoard);
        }

        /// <summary>
        /// Evaluates the node when it is not active.
        /// </summary>
        protected virtual BTState OnDeActiveEvaluate(IBlackBoard blackBoard)
        {
            for (int i = 0; i < Children.Count; i++)
            {
                var child = Children[i];
                if (child == null) continue;
                if (!child.CanReEvaluate) continue;
                if (child.Evaluate(blackBoard) == BTState.FAILURE)
                {
                    return BTState.FAILURE;
                }
            }
            return BTState.SUCCESS;
        }

        /// <summary>
        /// Evaluates the node when it is active (currently running).
        /// </summary>
        /// <param name="blackBoard">The blackboard instance</param>
        /// <returns>Evaluation result state</returns>
        protected abstract BTState OnActiveEvaluate(IBlackBoard blackBoard);

        /// <summary>
        /// Handles lower priority abort evaluation for conditional abort types.
        /// </summary>
        /// <param name="blackBoard">The blackboard instance</param>
        /// <returns>Evaluation result state</returns>
        protected virtual BTState OnLowerPriorityEvaluate(IBlackBoard blackBoard) => BTState.SUCCESS;

        /// <summary>
        /// Executes child nodes and returns the result state.
        /// </summary>
        protected abstract BTState RunChildren(IBlackBoard blackBoard);


        protected override void OnStop(IBlackBoard blackBoard)
        {
            for (int i = 0; i < Children.Count; i++)
            {
                Children[i]?.BTStop(blackBoard);
            }
        }

        protected override void CheckIntegrity()
        {
            for (int i = Children.Count - 1; i >= 0; i--)
            {
                if (Children[i] == null)
                {
                    Children.RemoveAt(i);
                }
            }

            Children.Sort(_positionComparer);
        }

        private class NodePositionComparer : IComparer<BTNode>
        {
            public int Compare(BTNode a, BTNode b)
            {
                if (a == null || b == null) return 0;
                return a.Position.x.CompareTo(b.Position.x);
            }
        }
    }
}