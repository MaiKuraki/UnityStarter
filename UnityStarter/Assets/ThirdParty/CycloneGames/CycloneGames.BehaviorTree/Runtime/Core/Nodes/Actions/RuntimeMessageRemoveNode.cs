namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Actions
{
    public class RuntimeMessageRemoveNode : RuntimeNode
    {
        public int KeyHash { get; set; }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            blackboard.Remove(KeyHash);
            return RuntimeState.Success;
        }
    }
}
