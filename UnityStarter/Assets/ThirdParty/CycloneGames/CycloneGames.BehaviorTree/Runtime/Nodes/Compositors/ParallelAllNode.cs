using CycloneGames.BehaviorTree.Runtime.Attributes;
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

        public override BTNode Clone()
        {
            var clone = (ParallelAllNode)base.Clone();
            clone._successThreshold = _successThreshold;
            clone._failureThreshold = _failureThreshold;
            return clone;
        }

    }
}
