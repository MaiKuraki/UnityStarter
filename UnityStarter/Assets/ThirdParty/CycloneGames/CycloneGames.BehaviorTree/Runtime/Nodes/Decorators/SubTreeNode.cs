using CycloneGames.BehaviorTree.Runtime.Attributes;
using CycloneGames.BehaviorTree.Runtime.Data;
using CycloneGames.BehaviorTree.Runtime.Interfaces;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Decorators
{
    [BTInfo("SubTree", "Runs another BehaviorTree as a subtree with its own scoped blackboard.")]
    public class SubTreeNode : DecoratorNode
    {
        [SerializeField] private BehaviorTree _subTreeAsset;

        protected override void OnStart(IBlackBoard blackBoard) { }

        protected override BTState OnRun(IBlackBoard blackBoard)
        {
            if (Child == null) return BTState.FAILURE;
            return Child.Run(blackBoard);
        }

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
            if (Child != null) node.Child = Child.CreateRuntimeNode();
            return node;
        }
    }
}
