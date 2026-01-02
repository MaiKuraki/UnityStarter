namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators
{
    public class RuntimeRepeatNode : RuntimeDecoratorNode
    {
        public bool RepeatForever { get; set; } = true;
        public int RepeatCount { get; set; } = 1;

        private int _currentRepeatCount;
        public int CurrentRepeatCount => _currentRepeatCount; // Exposed for debug

        protected override void OnStart(RuntimeBlackboard blackboard)
        {
            _currentRepeatCount = 0;
        }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            if (Child == null) return RuntimeState.Success;

            var state = Child.Run(blackboard);

            if (state == RuntimeState.Success || state == RuntimeState.Failure)
            {
                if (RepeatForever)
                {
                    _currentRepeatCount++;
                    // In original logic, RepeatNode typically restarts child.
                    // RuntimeNode.Run handles Start/Stop.
                    // If we return Running, the Runner calls us again next tick.
                    // But we need to reset the Child state to run it again!
                    Child.Abort(blackboard);
                    return RuntimeState.Running;
                }
                else
                {
                    _currentRepeatCount++;
                    if (_currentRepeatCount < RepeatCount)
                    {
                        Child.Abort(blackboard);
                        return RuntimeState.Running;
                    }
                    else
                    {
                        return RuntimeState.Success;
                    }
                }
            }

            return RuntimeState.Running;
        }
    }
}
