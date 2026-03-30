using CycloneGames.BehaviorTree.Runtime.Attributes;
using CycloneGames.BehaviorTree.Runtime.Data;
using CycloneGames.BehaviorTree.Runtime.Interfaces;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Compositors
{
    [BTInfo("Parallel", "Ticks all children each frame with configurable success/failure thresholds.")]
    public class ParallelAllNode : CompositeNode
    {
        [Tooltip("-1 = all children must succeed")]
        [SerializeField] private int _successThreshold = -1;
        [Tooltip("-1 = all children must fail")]
        [SerializeField] private int _failureThreshold = 1;

        protected override BTState OnActiveEvaluate(IBlackBoard blackBoard)
        {
            for (int i = 0; i < Children.Count; i++)
            {
                if (Children[i] == null || !Children[i].CanReEvaluate) continue;
                if (Children[i].Evaluate(blackBoard) == BTState.FAILURE) return BTState.FAILURE;
            }
            return BTState.SUCCESS;
        }

        protected override BTState RunChildren(IBlackBoard blackBoard)
        {
            if (Children.Count == 0) return BTState.SUCCESS;

            int successCount = 0;
            int failureCount = 0;
            int effectiveSuccess = _successThreshold < 0 ? Children.Count : _successThreshold;
            int effectiveFailure = _failureThreshold < 0 ? Children.Count : _failureThreshold;

            for (int i = 0; i < Children.Count; i++)
            {
                var state = Children[i].Run(blackBoard);
                if (state == BTState.SUCCESS) successCount++;
                else if (state == BTState.FAILURE) failureCount++;
            }

            if (successCount >= effectiveSuccess) return BTState.SUCCESS;
            if (failureCount >= effectiveFailure) return BTState.FAILURE;
            return BTState.RUNNING;
        }

        public override BTNode Clone()
        {
            var clone = (ParallelAllNode)base.Clone();
            clone._successThreshold = _successThreshold;
            clone._failureThreshold = _failureThreshold;
            return clone;
        }

        public override CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode CreateRuntimeNode()
        {
            var node = new CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors.RuntimeParallelAllNode();
            node.GUID = GUID;
            node.SuccessThreshold = _successThreshold;
            node.FailureThreshold = _failureThreshold;
            node.AbortType = (CycloneGames.BehaviorTree.Runtime.Core.RuntimeAbortType)(int)AbortType;
            foreach (var child in Children)
            {
                if (child != null) node.AddChild(child.CreateRuntimeNode());
            }
            return node;
        }
    }
}
