using CycloneGames.BehaviorTree.Runtime.Attributes;
using CycloneGames.BehaviorTree.Runtime.Data;
using CycloneGames.BehaviorTree.Runtime.Interfaces;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Compositors
{
    [BTInfo("Conditional", "N-way switch based on a blackboard int variable. Last child is the default branch.")]
    public class SwitchNode : CompositeNode
    {
        [SerializeField] private string _variableKey = "";

        protected override BTState OnActiveEvaluate(IBlackBoard blackBoard)
        {
            return BTState.SUCCESS;
        }

        protected override BTState RunChildren(IBlackBoard blackBoard)
        {
            if (Children.Count == 0) return BTState.FAILURE;

            int caseValue = -1;
            if (blackBoard != null && !string.IsNullOrEmpty(_variableKey))
            {
                caseValue = blackBoard.GetInt(_variableKey, -1);
            }

            int targetIndex;
            if (caseValue >= 0 && caseValue < Children.Count - 1)
                targetIndex = caseValue;
            else
                targetIndex = Children.Count - 1;

            if (Children[targetIndex] != null)
                return Children[targetIndex].Run(blackBoard);
            return BTState.FAILURE;
        }

        public override BTNode Clone()
        {
            var clone = (SwitchNode)base.Clone();
            clone._variableKey = _variableKey;
            return clone;
        }

        public override CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode CreateRuntimeNode()
        {
            var node = new CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors.RuntimeSwitchNode();
            node.GUID = GUID;
            node.VariableKeyHash = string.IsNullOrEmpty(_variableKey) ? 0 : _variableKey.GetHashCode();
            node.AbortType = (CycloneGames.BehaviorTree.Runtime.Core.RuntimeAbortType)(int)AbortType;
            foreach (var child in Children)
            {
                if (child != null) node.AddChild(child.CreateRuntimeNode());
            }
            return node;
        }
    }
}
