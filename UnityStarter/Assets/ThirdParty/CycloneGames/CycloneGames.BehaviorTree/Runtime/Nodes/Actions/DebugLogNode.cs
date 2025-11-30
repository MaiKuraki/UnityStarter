using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Nodes.Actions
{
    public class DebugLogNode : ActionNode
    {
        [SerializeField] private string _message = "";
        protected override void OnStart(BlackBoard blackBoard)
        {
#if UNITY_EDITOR
            if (!string.IsNullOrEmpty(_message)) Debug.Log("BT Log : " + _message);
#endif
        }

        public override BTNode Clone()
        {
            DebugLogNode node = (DebugLogNode)base.Clone();
            node._message = _message;
            return node;
        }
    }
}