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

        public override CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode CreateRuntimeNode()
        {
            var node = new CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators.RuntimeSubTreeNode();
            node.GUID = GUID;
            if (Child != null)
            {
                node.Child = CreateRequiredRuntimeNode(Child, "inline subtree child");
            }
            else if (_subTreeAsset != null && _subTreeAsset.Root != null)
            {
                node.Child = CreateRequiredRuntimeNode(_subTreeAsset.Root, "subtree asset root");
            }
            else
            {
                throw new System.InvalidOperationException("SubTreeNode requires an inline child or a subtree asset root.");
            }

            return node;
        }
    }
}
