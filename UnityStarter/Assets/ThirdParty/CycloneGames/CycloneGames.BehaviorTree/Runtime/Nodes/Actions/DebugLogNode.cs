using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Actions
{
    public class DebugLogNode : ActionNode
    {
        [SerializeField] private string _message = "";

        public override BTNode Clone()
        {
            DebugLogNode node = (DebugLogNode)base.Clone();
            node._message = _message;
            return node;
        }

        public override CycloneGames.BehaviorTree.Runtime.Core.RuntimeNode CreateRuntimeNode()
        {
            var node = new CycloneGames.BehaviorTree.Runtime.Core.Nodes.Actions.RuntimeDebugLogNode();
            node.GUID = GUID;
            node.Message = _message;
            return node;
        }
    }
}
