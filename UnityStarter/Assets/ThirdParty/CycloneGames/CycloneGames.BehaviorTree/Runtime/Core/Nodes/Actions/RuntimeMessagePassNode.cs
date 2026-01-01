using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Actions
{
    public class RuntimeMessagePassNode : RuntimeNode
    {
        public int KeyHash { get; set; }
        public string Message { get; set; }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            blackboard.SetObject(KeyHash, Message);
            return RuntimeState.Success;
        }
    }
}
