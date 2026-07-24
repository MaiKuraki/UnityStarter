using CycloneGames.BehaviorTree.Runtime.Attributes;
using CycloneGames.BehaviorTree.Runtime.Core;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Actions.BlackBoards
{
    [BTInfo("BlackBoard", "Passes a message to the blackboard with a specified key.")]
    public class MessagePassNode : ActionNode
    {
        [SerializeField, BehaviorTreeBlackboardKey(RuntimeBlackboardValueType.Object)]
        private string _key = "Key";
        [SerializeField] private string _message = "Message";

        public string Key => _key;
        public string Message => _message;

        public override BTNode Clone()
        {
            var clone = (MessagePassNode)base.Clone();
            clone._key = _key;
            clone._message = _message;
            return clone;
        }

    }
}
