using CycloneGames.BehaviorTree.Runtime.Attributes;
using CycloneGames.BehaviorTree.Runtime.Core;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Compositors
{
    [BTInfo("Conditional", "N-way switch based on a blackboard int variable. Last child is the default branch.")]
    public class SwitchNode : CompositeNode
    {
        [SerializeField, BehaviorTreeBlackboardKey(RuntimeBlackboardValueType.Int)]
        private string _variableKey = "";

        public string VariableKey => _variableKey;

        public override BTNode Clone()
        {
            var clone = (SwitchNode)base.Clone();
            clone._variableKey = _variableKey;
            return clone;
        }

    }
}
