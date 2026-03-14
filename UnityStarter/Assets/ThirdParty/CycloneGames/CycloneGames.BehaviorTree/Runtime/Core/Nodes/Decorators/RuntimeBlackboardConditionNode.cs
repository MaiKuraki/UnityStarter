namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators
{
    public class RuntimeBlackboardConditionNode : RuntimeDecoratorNode
    {
        public int KeyHash { get; set; }

        public override bool CanEvaluate => true;

        public override bool Evaluate(RuntimeBlackboard blackboard)
        {
            if (!blackboard.HasKey(KeyHash)) return false;
            var obj = blackboard.GetObject<object>(KeyHash);
            return obj != null;
        }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            if (Child == null) return RuntimeState.Failure;
            if (!Evaluate(blackboard)) return RuntimeState.Failure;

            return Child.Run(blackboard);
        }
    }
}