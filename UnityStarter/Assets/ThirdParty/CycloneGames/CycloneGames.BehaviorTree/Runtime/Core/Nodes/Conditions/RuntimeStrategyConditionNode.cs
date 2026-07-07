using System;

namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Conditions
{
    public sealed class RuntimeStrategyConditionNode : RuntimeNode
    {
        private readonly IRuntimeBTConditionStrategy _strategy;

        public RuntimeStrategyConditionNode(IRuntimeBTConditionStrategy strategy)
        {
            _strategy = strategy ?? throw new ArgumentNullException(nameof(strategy));
        }

        public string Name { get; set; }

        public override bool CanEvaluate => true;

        public override bool Evaluate(RuntimeBlackboard blackboard)
        {
            return _strategy.Evaluate(blackboard);
        }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            return _strategy.Evaluate(blackboard) ? RuntimeState.Success : RuntimeState.Failure;
        }
    }
}
