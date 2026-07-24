using CycloneGames.BehaviorTree.Runtime.Attributes;
using CycloneGames.BehaviorTree.Runtime.Core;
using CycloneGames.BehaviorTree.Runtime.Nodes;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Conditions.BlackBoards
{
    [BTInfo("BlackBoard", "Checks if a message received from the blackboard matches a specified value.")]
    public class MessageReceiveNode : ConditionNode
    {
        [SerializeField, BehaviorTreeBlackboardKey(RuntimeBlackboardValueType.Object)]
        private string _key = "Key";
        [SerializeField] private string _message = "Message";

        public string Key => _key;
        public string Message => _message;

        public override BTNode Clone()
        {
            var clone = (MessageReceiveNode)base.Clone();
            clone._key = _key;
            clone._message = _message;
            return clone;
        }

    }
}
