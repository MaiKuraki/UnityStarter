using System;

namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Actions
{
    public sealed class RuntimeCommandActionNode : RuntimeNode
    {
        private readonly IRuntimeBTCommand _command;

        public RuntimeCommandActionNode(IRuntimeBTCommand command)
        {
            _command = command ?? throw new ArgumentNullException(nameof(command));
        }

        private string _name;
        public string Name
        {
            get => _name;
            set => SetSetupValue(ref _name, value);
        }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            return _command.Execute(blackboard);
        }
    }
}
