using System;

namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators
{
    public sealed class RuntimeLambdaDecoratorNode : RuntimeDecoratorNode
    {
        private readonly Func<RuntimeBlackboard, RuntimeNode, RuntimeState> _run;

        public RuntimeLambdaDecoratorNode(Func<RuntimeBlackboard, RuntimeNode, RuntimeState> run)
        {
            _run = run ?? throw new ArgumentNullException(nameof(run));
        }

        public string Name { get; set; }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            return _run(blackboard, Child);
        }
    }
}
