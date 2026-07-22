namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Actions
{
    public class RuntimeMessagePassNode : RuntimeNode
    {
        private int _keyHash;
        private string _message;

        public int KeyHash
        {
            get => _keyHash;
            set => SetSetupValue(ref _keyHash, value);
        }

        public string Message
        {
            get => _message;
            set => SetSetupValue(ref _message, value);
        }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            blackboard.SetObject(KeyHash, Message);
            return RuntimeState.Success;
        }
    }
}
