using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Actions
{
    public class DebugLogNode : ActionNode
    {
        [SerializeField] private string _message = "";

        public string Message => _message;

        public override BTNode Clone()
        {
            DebugLogNode node = (DebugLogNode)base.Clone();
            node._message = _message;
            return node;
        }

    }
}
