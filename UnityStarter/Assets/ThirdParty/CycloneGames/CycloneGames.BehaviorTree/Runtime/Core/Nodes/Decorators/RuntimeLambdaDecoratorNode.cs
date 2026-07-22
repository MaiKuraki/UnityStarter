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

        private string _name;
        public string Name
        {
            get => _name;
            set => SetSetupValue(ref _name, value);
        }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            return _run(blackboard, Child);
        }
    }
}
