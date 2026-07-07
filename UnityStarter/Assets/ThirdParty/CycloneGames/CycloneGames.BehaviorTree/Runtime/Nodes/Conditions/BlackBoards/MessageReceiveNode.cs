using CycloneGames.BehaviorTree.Runtime.Attributes;
using CycloneGames.BehaviorTree.Runtime.Nodes;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Conditions.BlackBoards
{
    [BTInfo("BlackBoard", "Checks if a message received from the blackboard matches a specified value.")]
    public class MessageReceiveNode : ConditionNode
    {
        [SerializeField] private string _key = "Key";
        [SerializeField] private string _message = "Message";

        public override BTNode Clone()
        {
            var clone = (MessageReceiveNode)base.Clone();
            clone._key = _key;
            clone._message = _message;
            return clone;
        }

        public override CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode CreateRuntimeNode()
        {
            var node = new CycloneGames.BehaviorTree.Runtime.Core.Nodes.Conditions.RuntimeMessageReceiveNode();
            node.GUID = GUID;
            node.KeyHash = UnityEngine.Animator.StringToHash(_key);
            node.ExpectedMessage = _message;
            return node;
        }
    }
}
