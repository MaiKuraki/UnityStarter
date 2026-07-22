namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Conditions
{
    public class RuntimeOnOffNode : RuntimeNode
    {
        private bool _isOn;

        public bool IsOn
        {
            get => _isOn;
            set => SetSetupValue(ref _isOn, value);
        }

        public override bool CanEvaluate => true;
        public override bool Evaluate(RuntimeBlackboard blackboard) => IsOn;

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            return IsOn ? RuntimeState.Success : RuntimeState.Failure;
        }
    }
}
