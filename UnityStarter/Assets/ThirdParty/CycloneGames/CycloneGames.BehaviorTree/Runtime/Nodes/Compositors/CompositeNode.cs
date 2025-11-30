using System.Collections.Generic;
using CycloneGames.BehaviorTree.Runtime.Data;
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

        public override BTNode Clone()
        {
            var clone = (CompositeNode)base.Clone();

            if (Application.isPlaying)
            {
                // Manual loop instead of ConvertAll to avoid Lambda allocation
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

        protected override BTState OnRun(BlackBoard blackBoard)
        {
            // Self abort
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

        public override BTState Evaluate(BlackBoard blackBoard)
        {
            return IsStarted ? OnActiveEvaluate(blackBoard) : OnDeActiveEvaluate(blackBoard);
        }

        /// <summary>
        /// Evaluate the node when it is not active
        /// </summary>
        /// <param name="blackBoard"> BlackBoard </param>
        /// <returns> BTState </returns>
        protected virtual BTState OnDeActiveEvaluate(BlackBoard blackBoard)
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

        protected abstract BTState OnActiveEvaluate(BlackBoard blackBoard);

        /// <summary>
        /// Handle the low priority abort
        /// </summary>
        /// <param name="blackBoard"> BlackBoard </param>
        protected virtual BTState OnLowerPriorityEvaluate(BlackBoard blackBoard) => BTState.SUCCESS;

        /// <summary>
        /// Handle the children nodes
        /// </summary>
        /// <param name="blackBoard"> BlackBoard </param>
        /// <returns> BTState </returns>
        protected abstract BTState RunChildren(BlackBoard blackBoard);


        protected override void OnStop(BlackBoard blackBoard)
        {
            for (int i = 0; i < Children.Count; i++)
            {
                Children[i]?.BTStop(blackBoard);
            }
        }

        protected override void CheckIntegrity()
        {
            // Manual loop instead of RemoveAll to avoid Lambda allocation
            for (int i = Children.Count - 1; i >= 0; i--)
            {
                if (Children[i] == null)
                {
                    Children.RemoveAt(i);
                }
            }

            // Use cached IComparer instead of Lambda
            Children.Sort(_positionComparer);
        }

        // Reusable comparer to avoid Lambda allocation
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