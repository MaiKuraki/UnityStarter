using System;

namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Conditions
{
    public class RuntimeMessageReceiveNode : RuntimeNode
    {
        private int _keyHash;
        private string _expectedMessage;

        public int KeyHash
        {
            get => _keyHash;
            set => SetSetupValue(ref _keyHash, value);
        }

        public string ExpectedMessage
        {
            get => _expectedMessage;
            set => SetSetupValue(ref _expectedMessage, value);
        }

        public override bool CanEvaluate => true;

        public override bool Evaluate(RuntimeBlackboard blackboard)
        {
            var message = blackboard.GetObject<string>(KeyHash);
            if (message == null) return false;
            return string.Equals(message, ExpectedMessage, StringComparison.Ordinal);
        }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            return Evaluate(blackboard) ? RuntimeState.Success : RuntimeState.Failure;
        }
    }
}
