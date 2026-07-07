using CycloneGames.BehaviorTree.Runtime.Attributes;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Actions.BlackBoards
{
    [BTInfo("BlackBoard", "Removes a key-value pair from the blackboard.")]
    public class MessageRemoveNode : ActionNode
    {
        [SerializeField] private string _key;

        public override BTNode Clone()
        {
            var clone = (MessageRemoveNode)base.Clone();
            clone._key = _key;
            return clone;
        }

        public override CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode CreateRuntimeNode()
        {
            var node = new CycloneGames.BehaviorTree.Runtime.Core.Nodes.Actions.RuntimeMessageRemoveNode();
            node.GUID = GUID;
            node.KeyHash = UnityEngine.Animator.StringToHash(_key);
            return node;
        }
    }
}
