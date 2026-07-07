using System;

namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Conditions
{
    public sealed class RuntimeLambdaConditionNode : RuntimeNode
    {
        private readonly Func<RuntimeBlackboard, bool> _predicate;

        public RuntimeLambdaConditionNode(Func<RuntimeBlackboard, bool> predicate)
        {
            _predicate = predicate ?? throw new ArgumentNullException(nameof(predicate));
        }

        public string Name { get; set; }

        public override bool CanEvaluate => true;

        public override bool Evaluate(RuntimeBlackboard blackboard)
        {
            return _predicate(blackboard);
        }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            return _predicate(blackboard) ? RuntimeState.Success : RuntimeState.Failure;
        }
    }
}
