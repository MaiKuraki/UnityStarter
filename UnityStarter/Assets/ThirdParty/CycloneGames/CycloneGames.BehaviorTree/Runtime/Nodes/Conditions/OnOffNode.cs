using CycloneGames.BehaviorTree.Runtime.Data;
using CycloneGames.BehaviorTree.Runtime.Interfaces;
using CycloneGames.BehaviorTree.Runtime.Nodes;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Conditions
{
    public class OnOffNode : ConditionNode
    {
        [SerializeField] private bool _on = true;
        protected override BTState GetConditionState(IBlackBoard blackBoard)
        {
            return _on ? BTState.SUCCESS : BTState.FAILURE;
        }
        public override BTNode Clone()
        {
            var clone = (OnOffNode)base.Clone();
            clone._on = _on;
            return clone;
        }
    }
}