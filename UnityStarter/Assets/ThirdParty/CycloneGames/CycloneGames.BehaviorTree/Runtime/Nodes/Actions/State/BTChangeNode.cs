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

        public override CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode CreateRuntimeNode()
        {
            var node = new CycloneGames.BehaviorTree.Runtime.Core.Nodes.Actions.State.RuntimeBTChangeNode();
            node.GUID = GUID;
            node.StateId = _stateId;
            return node;
        }
    }
}