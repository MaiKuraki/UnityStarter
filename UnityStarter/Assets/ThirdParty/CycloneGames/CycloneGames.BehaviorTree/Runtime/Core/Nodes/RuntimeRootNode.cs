namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes
{
    public class RuntimeRootNode : RuntimeNode
    {
        public RuntimeNode Child { get; set; }

        public override void OnAwake()
        {
            if (Child != null)
            {
                Child.OnAwake();
            }
        }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            if (Child == null) return RuntimeState.Failure;
            return Child.Run(blackboard);
        }
        
        protected override void OnStop(RuntimeBlackboard blackboard)
        {
            if (Child != null && Child.IsStarted)
            {
                Child.Abort(blackboard);
            }
        }
    }
}
