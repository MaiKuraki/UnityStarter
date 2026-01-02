namespace CycloneGames.BehaviorTree.Runtime.Core
{
    /// <summary>
    /// 0GC tick manager for large-scale AI (1000+ agents).
    /// Uses pre-allocated array with round-robin distribution.
    /// </summary>
    public class BTTickManager
    {
        private const int DEFAULT_CAPACITY = 1024;

        private RuntimeBehaviorTree[] _trees;
        private int _capacity;
        private int _count;
        private int _currentIndex;

        public int TickBudget { get; set; } = 100;
        public int Count => _count;

        public BTTickManager(int initialCapacity = DEFAULT_CAPACITY)
        {
            _capacity = initialCapacity;
            _trees = new RuntimeBehaviorTree[_capacity];
            _count = 0;
            _currentIndex = 0;
        }

        public void Register(RuntimeBehaviorTree tree)
        {
            if (tree == null) return;

            // Check if already registered
            for (int i = 0; i < _count; i++)
            {
                if (_trees[i] == tree) return;
            }

            // Expand if needed (rare, amortized O(1))
            if (_count >= _capacity)
            {
                int newCapacity = _capacity * 2;
                var newArray = new RuntimeBehaviorTree[newCapacity];
                for (int i = 0; i < _count; i++)
                {
                    newArray[i] = _trees[i];
                }
                _trees = newArray;
                _capacity = newCapacity;
            }

            _trees[_count] = tree;
            _count++;
        }

        public void Unregister(RuntimeBehaviorTree tree)
        {
            if (tree == null) return;

            for (int i = 0; i < _count; i++)
            {
                if (_trees[i] == tree)
                {
                    // Swap with last element for O(1) removal
                    _count--;
                    _trees[i] = _trees[_count];
                    _trees[_count] = null;

                    // Adjust index if needed
                    if (_currentIndex >= _count && _count > 0)
                    {
                        _currentIndex = 0;
                    }
                    return;
                }
            }
        }

        public void Tick()
        {
            if (_count == 0) return;

            int tickedCount = 0;
            int budget = TickBudget > 0 ? TickBudget : _count;

            while (tickedCount < budget && tickedCount < _count)
            {
                var tree = _trees[_currentIndex];
                if (tree != null && tree.ShouldTick())
                {
                    tree.Tick();
                }

                _currentIndex++;
                if (_currentIndex >= _count)
                {
                    _currentIndex = 0;
                }
                tickedCount++;
            }
        }

        public void Clear()
        {
            for (int i = 0; i < _count; i++)
            {
                _trees[i] = null;
            }
            _count = 0;
            _currentIndex = 0;
        }
    }
}
