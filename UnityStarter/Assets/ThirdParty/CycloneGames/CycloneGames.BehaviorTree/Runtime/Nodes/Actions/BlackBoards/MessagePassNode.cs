using CycloneGames.BehaviorTree.Runtime.Attributes;
using CycloneGames.BehaviorTree.Runtime.Data;
using CycloneGames.BehaviorTree.Runtime.Interfaces;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Actions.BlackBoards
{
    [BTInfo("BlackBoard", "Passes a message to the blackboard with a specified key.")]
    public class MessagePassNode : ActionNode
    {
        [SerializeField] private string _key = "Key";
        [SerializeField] private string _message = "Message";
        protected override BTState OnRun(IBlackBoard blackBoard)
        {
            //Debug.Log($"MessagePassNode : {_key} : {_message}");
            blackBoard?.Set(_key, _message);
            return BTState.SUCCESS;
        }

        public override BTNode Clone()
        {
            var clone = (MessagePassNode)base.Clone();
            clone._key = _key;
            clone._message = _message;
            return clone;
        }

        public override CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode CreateRuntimeNode()
        {
            var node = new CycloneGames.BehaviorTree.Runtime.Core.Nodes.Actions.RuntimeMessagePassNode();
            node.GUID = GUID;
            node.KeyHash = UnityEngine.Animator.StringToHash(_key);
            node.Message = _message;
            return node;
        }
    }
}