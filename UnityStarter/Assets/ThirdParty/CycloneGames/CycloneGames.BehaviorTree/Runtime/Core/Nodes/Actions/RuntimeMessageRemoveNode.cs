namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Actions
{
    public class RuntimeMessageRemoveNode : RuntimeNode
    {
        public int KeyHash { get; set; }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            // RuntimeBlackboard currently doesn't support Remove for 0GC design
            // If needed, we can add a Remove method later.
            // For now, this is effectively a no-op success.
            return RuntimeState.Success;
        }
    }
}
