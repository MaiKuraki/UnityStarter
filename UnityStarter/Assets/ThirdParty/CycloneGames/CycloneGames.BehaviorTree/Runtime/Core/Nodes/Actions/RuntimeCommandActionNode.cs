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

        public string Name { get; set; }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            return _command.Execute(blackboard);
        }
    }
}
