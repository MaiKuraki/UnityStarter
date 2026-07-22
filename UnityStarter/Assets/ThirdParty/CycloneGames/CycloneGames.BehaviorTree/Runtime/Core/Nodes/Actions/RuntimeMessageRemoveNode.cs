namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Actions
{
    public class RuntimeMessageRemoveNode : RuntimeNode
    {
        private int _keyHash;

        public int KeyHash
        {
            get => _keyHash;
            set => SetSetupValue(ref _keyHash, value);
        }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            blackboard.Remove(KeyHash);
            return RuntimeState.Success;
        }
    }
}
