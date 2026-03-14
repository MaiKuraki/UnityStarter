namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators
{
    public class RuntimeBlackboardNode : RuntimeDecoratorNode
    {
        private RuntimeBlackboard _scopedBlackboard;

        public override void OnAwake()
        {
            base.OnAwake();
            _scopedBlackboard = new RuntimeBlackboard();
        }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            if (Child == null) return RuntimeState.Success;

            _scopedBlackboard.Parent = blackboard;
            return Child.Run(_scopedBlackboard);
        }

        protected override void OnStop(RuntimeBlackboard blackboard)
        {
            base.OnStop(blackboard);
            _scopedBlackboard?.Clear();
        }
    }
}
