namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators
{
    public abstract class RuntimeDecoratorNode : RuntimeNode
    {
        public RuntimeNode Child { get; set; }

        public override bool CanEvaluate => Child != null && Child.CanEvaluate;

        public override bool Evaluate(RuntimeBlackboard blackboard)
        {
            if (Child == null || !Child.CanEvaluate) return true;
            return Child.Evaluate(blackboard);
        }

        public override void OnAwake()
        {
            if (Child != null)
            {
                Child.OnAwake();
            }
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
