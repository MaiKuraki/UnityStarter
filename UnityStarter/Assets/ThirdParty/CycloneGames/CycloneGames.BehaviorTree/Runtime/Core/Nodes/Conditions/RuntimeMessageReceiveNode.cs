namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Conditions
{
    public class RuntimeMessageReceiveNode : RuntimeNode
    {
        public int KeyHash { get; set; }
        public string ExpectedMessage { get; set; }

        public override bool CanEvaluate => true;

        public override bool Evaluate(RuntimeBlackboard blackboard)
        {
            var message = blackboard.GetObject<string>(KeyHash);
            if (message == null) return false;
            return message.Equals(ExpectedMessage);
        }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            return Evaluate(blackboard) ? RuntimeState.Success : RuntimeState.Failure;
        }
    }
}
