using CycloneGames.BehaviorTree.Runtime.Attributes;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Compositors
{
    [BTInfo("Conditional", "N-way switch based on a blackboard int variable. Last child is the default branch.")]
    public class SwitchNode : CompositeNode
    {
        [SerializeField] private string _variableKey = "";

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
            node.VariableKeyHash = string.IsNullOrEmpty(_variableKey) ? 0 : Animator.StringToHash(_variableKey);
            AddRuntimeChildren(node);
            return node;
        }
    }
}
