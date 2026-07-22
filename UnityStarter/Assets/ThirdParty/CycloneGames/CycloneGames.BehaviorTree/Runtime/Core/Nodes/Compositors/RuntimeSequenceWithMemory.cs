namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors
{
    /// <summary>
    /// Like Sequencer but skips children that have already succeeded.
    /// Only re-ticks children still in RUNNING or NOT_ENTERED state.
    /// Returns SUCCESS once all children have succeeded.
    /// </summary>
    public class RuntimeSequenceWithMemory : RuntimeCompositeNode
    {
        private int _current;

        // Per-child completion tracking (pre-allocated in Seal/OnAwake)
        private bool[] _completed;

        public override void OnAwake()
        {
            base.OnAwake();
            _completed = ChildCount == 0 ? System.Array.Empty<bool>() : new bool[ChildCount];
        }

        protected override void OnStart(RuntimeBlackboard blackboard)
        {
            _current = 0;
            RuntimeNode[] children = ChildArray;
            for (int i = 0; i < _completed.Length; i++)
            {
                _completed[i] = false;
                children[i].PrepareForActivation();
            }
        }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            RuntimeNode[] children = ChildArray;

            while (_current < children.Length)
            {
                if (_completed[_current])
                {
                    _current++;
                    continue;
                }

                var state = children[_current].Run(blackboard);

                if (state == RuntimeState.Failure)
                    return RuntimeState.Failure;

                if (state == RuntimeState.Running)
                    return RuntimeState.Running;

                // SUCCESS
                _completed[_current] = true;
                _current++;
            }

            return RuntimeState.Success;
        }

        protected override void OnExit(RuntimeBlackboard blackboard, RuntimeNodeExitReason reason, System.Exception exception)
        {
            RuntimeNode[] children = ChildArray;
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].IsStarted) children[i].Abort(blackboard);
            }
        }
    }
}
