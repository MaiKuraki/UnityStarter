namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators
{
    public class RuntimeBlackboardNode : RuntimeDecoratorNode
    {
        private RuntimeBlackboard _scopedBlackboard;

        public override void OnAwake()
        {
            base.OnAwake();
            // Pre-allocate or prepare? 
            // We can't set parent yet as we don't know the parent runtime blackboard in Awake.
        }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            if (Child == null) return RuntimeState.Success;

            // Initialize scoped blackboard if needed or update parent
            if (_scopedBlackboard == null)
            {
                _scopedBlackboard = new RuntimeBlackboard(blackboard);
            }
            else if (_scopedBlackboard.Parent != blackboard)
            {
                 // Reuse but ensure parent is correct (e.g. if tree context changes, though unlikely for same node instance)
                 // Start with new parent if we can't easily re-parent (RuntimeBlackboard properties are private? No, generic getter/setter)
                 // Actually RuntimeBlackboard.Parent is read-only in my implementation.
                 // So if parent changes, we might need new instance.
                 // But typically parent blackboard is stable per tree run.
                 if (_scopedBlackboard.Parent != blackboard)
                 {
                     _scopedBlackboard = new RuntimeBlackboard(blackboard);
                 }
            }

            return Child.Run(_scopedBlackboard);
        }
        
        protected override void OnStop(RuntimeBlackboard blackboard)
        {
            base.OnStop(blackboard);
            _scopedBlackboard?.Clear(); // Clear local scope data on stop? Or keep it? 
            // Original BlackBoardNode does Clear() in OnStop.
        }
    }
}
