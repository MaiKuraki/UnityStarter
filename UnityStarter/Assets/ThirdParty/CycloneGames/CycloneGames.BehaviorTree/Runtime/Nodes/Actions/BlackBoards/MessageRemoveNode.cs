using CycloneGames.BehaviorTree.Runtime.Attributes;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Actions.BlackBoards
{
    [BTInfo("BlackBoard", "Removes a key-value pair from the blackboard.")]
    public class MessageRemoveNode : ActionNode
    {
        [SerializeField, BehaviorTreeBlackboardKey]
        private string _key;

        public string Key => _key;

        public override BTNode Clone()
        {
            var clone = (MessageRemoveNode)base.Clone();
            clone._key = _key;
            return clone;
        }

    }
}
