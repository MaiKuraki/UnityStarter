namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors
{
    public class RuntimeSelector : RuntimeCompositeNode
    {
        private int _current;
        public int CurrentIndex => _current;

        protected override void OnStart(RuntimeBlackboard blackboard)
        {
            _current = 0;
        }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            for (int i = _current; i < Children.Count; i++)
            {
                var child = Children[i];
                var state = child.Run(blackboard);

                if (state == RuntimeState.Running)
                {
                    _current = i;
                    return RuntimeState.Running;
                }
                
                if (state == RuntimeState.Success)
                {
                    return RuntimeState.Success;
                }
            }

            return RuntimeState.Failure;
        }
        
        protected override void OnStop(RuntimeBlackboard blackboard)
        {
             if (_current < Children.Count)
             {
                 if (Children[_current].State == RuntimeState.Running)
                 {
                     Children[_current].Abort(blackboard);
                 }
             }
        }
    }
}
