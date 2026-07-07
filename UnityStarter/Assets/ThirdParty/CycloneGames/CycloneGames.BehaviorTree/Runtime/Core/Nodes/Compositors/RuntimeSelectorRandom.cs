namespace CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors
{
    public sealed class RuntimeSelectorRandom : RuntimeCompositeNode
    {
        private int[] _order;
        private int _currentIndex;
        private RuntimeDeterministicRandom _deterministicRandom;

        public RuntimeSelectorRandom(uint seed = 0u)
        {
            Seed = seed;
            _deterministicRandom = new RuntimeDeterministicRandom(seed);
        }

        public uint Seed { get; set; }
        public bool ShuffleOnStart { get; set; } = true;
        public override int CurrentIndex => _currentIndex;

        public override void OnAwake()
        {
            base.OnAwake();

            RuntimeNode[] children = Children;
            int count = children != null ? children.Length : 0;
            _order = new int[count];
            for (int i = 0; i < count; i++)
            {
                _order[i] = i;
            }
        }

        protected override void OnStart(RuntimeBlackboard blackboard)
        {
            _currentIndex = 0;
            if (ShuffleOnStart)
            {
                Shuffle(blackboard);
            }
        }

        protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
        {
            RuntimeNode[] children = Children;
            if (children == null || children.Length == 0)
            {
                return RuntimeState.Failure;
            }

            while (_currentIndex < _order.Length)
            {
                RuntimeNode child = children[_order[_currentIndex]];
                RuntimeState state = child.Run(blackboard);
                if (state == RuntimeState.Success)
                {
                    return RuntimeState.Success;
                }

                if (state == RuntimeState.Running)
                {
                    return RuntimeState.Running;
                }

                _currentIndex++;
            }

            return RuntimeState.Failure;
        }

        protected override void OnStop(RuntimeBlackboard blackboard)
        {
            RuntimeNode[] children = Children;
            if (children == null)
            {
                return;
            }

            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].IsStarted)
                {
                    children[i].Abort(blackboard);
                }
            }
        }

        private void Shuffle(RuntimeBlackboard blackboard)
        {
            if (_order == null)
            {
                return;
            }

            for (int i = _order.Length - 1; i > 0; i--)
            {
                int selectedIndex = NextIndex(blackboard, i + 1);
                int value = _order[selectedIndex];
                _order[selectedIndex] = _order[i];
                _order[i] = value;
            }
        }

        private int NextIndex(RuntimeBlackboard blackboard, int maxExclusive)
        {
            if (Seed != 0u)
            {
                return _deterministicRandom.NextInt(maxExclusive);
            }

            return RuntimeRandomUtility.RangeInt(blackboard, 0, maxExclusive);
        }
    }
}
