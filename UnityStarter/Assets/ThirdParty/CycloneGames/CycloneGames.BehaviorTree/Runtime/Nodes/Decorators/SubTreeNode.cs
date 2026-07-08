using CycloneGames.BehaviorTree.Runtime.Attributes;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Decorators
{
    [BTInfo("SubTree", "Runs another BehaviorTree as a subtree with its own scoped blackboard.")]
    public class SubTreeNode : DecoratorNode
    {
        [SerializeField] private BehaviorTree _subTreeAsset;

        public BehaviorTree SubTreeAsset => _subTreeAsset;

        public override BTNode Clone()
        {
            var clone = (SubTreeNode)base.Clone();
            clone._subTreeAsset = _subTreeAsset;
            return clone;
        }

    }
}
