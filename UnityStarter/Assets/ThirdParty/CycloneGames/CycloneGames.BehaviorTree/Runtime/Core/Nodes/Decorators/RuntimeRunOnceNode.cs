namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators
{
    /// <summary>
    /// Executes child once, then returns the cached result on subsequent ticks
    /// without re-executing. Reset via Abort.
    /// </summary>
    public class RuntimeRunOnceNode : RuntimeDecoratorNode
    {
        private bool _hasRun;
        private RuntimeState _cachedResult;

        protected override void OnStart(RuntimeBlackboard blackboard)
        {
            // Reset only happens via Abort, not re-entry within same tree lifetime
        }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            if (_hasRun) return _cachedResult;

            if (Child == null) return RuntimeState.Failure;

            var state = Child.Run(blackboard);
            if (state != RuntimeState.Running)
            {
                _hasRun = true;
                _cachedResult = state;
            }
            return state;
        }

        protected override void OnStop(RuntimeBlackboard blackboard)
        {
            base.OnStop(blackboard);
            _hasRun = false;
            _cachedResult = RuntimeState.NotEntered;
        }
    }
}
