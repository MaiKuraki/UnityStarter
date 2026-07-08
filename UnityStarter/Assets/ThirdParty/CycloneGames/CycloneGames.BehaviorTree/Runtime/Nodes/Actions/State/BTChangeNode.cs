using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Actions.State
{
    public class BTChangeNode : ActionNode
    {
        [SerializeField] private string _stateId;

        public override BTNode Clone()
        {
            var clone = (BTChangeNode)base.Clone();
            clone._stateId = _stateId;
            return clone;
        }

    }
}