namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Conditions
{
    public class RuntimeOnOffNode : RuntimeNode
    {
        public bool IsOn { get; set; }

        public override bool CanEvaluate => true;
        public override bool Evaluate(RuntimeBlackboard blackboard) => IsOn;

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            return IsOn ? RuntimeState.Success : RuntimeState.Failure;
        }
    }
}