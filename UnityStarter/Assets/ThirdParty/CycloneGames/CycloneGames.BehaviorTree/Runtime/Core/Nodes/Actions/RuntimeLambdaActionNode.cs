using System;

namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Actions
{
    public sealed class RuntimeLambdaActionNode : RuntimeNode
    {
        private readonly Func<RuntimeBlackboard, RuntimeState> _run;
        private readonly Action<RuntimeBlackboard> _onStart;
        private readonly Action<RuntimeBlackboard> _onStop;

        public RuntimeLambdaActionNode(
            Func<RuntimeBlackboard, RuntimeState> run,
            Action<RuntimeBlackboard> onStart = null,
            Action<RuntimeBlackboard> onStop = null)
        {
            _run = run ?? throw new ArgumentNullException(nameof(run));
            _onStart = onStart;
            _onStop = onStop;
        }

        private string _name;
        public string Name
        {
            get => _name;
            set => SetSetupValue(ref _name, value);
        }

        protected override void OnStart(RuntimeBlackboard blackboard)
        {
            _onStart?.Invoke(blackboard);
        }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            return _run(blackboard);
        }

        protected override void OnExit(
            RuntimeBlackboard blackboard,
            RuntimeNodeExitReason reason,
            Exception exception)
        {
            _onStop?.Invoke(blackboard);
        }
    }
}
