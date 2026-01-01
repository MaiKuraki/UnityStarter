namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Conditions
{
    public class RuntimeMessageReceiveNode : RuntimeNode
    {
        public int KeyHash { get; set; }
        public string ExpectedMessage { get; set; }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            var message = blackboard.GetObject<string>(KeyHash);
            if (message == null) return RuntimeState.Failure;
            return message.Equals(ExpectedMessage) ? RuntimeState.Success : RuntimeState.Failure;
        }
    }
}
