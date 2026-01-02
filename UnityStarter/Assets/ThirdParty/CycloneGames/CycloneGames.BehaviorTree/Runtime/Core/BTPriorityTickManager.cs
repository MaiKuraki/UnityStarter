using System.Collections.Generic;

namespace CycloneGames.BehaviorTree.Runtime.Core
{
    /// <summary>
    /// Priority-based tick manager for large-scale AI (1000+ agents).
    /// Supports LOD, priority buckets, and event-driven priority boost.
    /// </summary>
    public class BTPriorityTickManager
    {
        private const int DEFAULT_CAPACITY = 256;
        private const int MAX_PRIORITY_LEVELS = 8;

        // Priority buckets: each bucket is a list of trees at that priority level
        private readonly List<RuntimeBehaviorTree>[] _buckets;
        private readonly int[] _bucketIndices;
        private readonly int[] _budgets;

        private int _priorityLevelCount;

        public BTPriorityTickManager(int[] budgets = null)
        {
            _priorityLevelCount = MAX_PRIORITY_LEVELS;
            _buckets = new List<RuntimeBehaviorTree>[MAX_PRIORITY_LEVELS];
            _bucketIndices = new int[MAX_PRIORITY_LEVELS];
            _budgets = budgets ?? new int[] { 100, 50, 30, 20, 15, 10, 5, 5 };

            for (int i = 0; i < MAX_PRIORITY_LEVELS; i++)
            {
                _buckets[i] = new List<RuntimeBehaviorTree>(DEFAULT_CAPACITY);
                _bucketIndices[i] = 0;
            }
        }

        public void SetBudgets(int[] budgets)
        {
            if (budgets == null) return;
            for (int i = 0; i < budgets.Length && i < MAX_PRIORITY_LEVELS; i++)
            {
                _budgets[i] = budgets[i];
            }
        }

        public void Register(RuntimeBehaviorTree tree, int priority = 0)
        {
            if (tree == null) return;
            priority = ClampPriority(priority);

            // Check if already registered in any bucket
            for (int i = 0; i < _priorityLevelCount; i++)
            {
                if (_buckets[i].Contains(tree))
                {
                    if (i == priority) return;
                    _buckets[i].Remove(tree);
                    break;
                }
            }

            _buckets[priority].Add(tree);
        }

        public void Unregister(RuntimeBehaviorTree tree)
        {
            if (tree == null) return;

            for (int i = 0; i < _priorityLevelCount; i++)
            {
                if (_buckets[i].Remove(tree))
                {
                    if (_bucketIndices[i] >= _buckets[i].Count && _buckets[i].Count > 0)
                    {
                        _bucketIndices[i] = 0;
                    }
                    return;
                }
            }
        }

        public void UpdatePriority(RuntimeBehaviorTree tree, int newPriority)
        {
            Register(tree, newPriority);
        }

        public void Tick()
        {
            // Tick each priority level with its budget
            for (int priority = 0; priority < _priorityLevelCount; priority++)
            {
                var bucket = _buckets[priority];
                int count = bucket.Count;
                if (count == 0) continue;

                int budget = priority < _budgets.Length ? _budgets[priority] : 5;
                int tickedCount = 0;

                while (tickedCount < budget && tickedCount < count)
                {
                    int idx = _bucketIndices[priority];
                    var tree = bucket[idx];

                    if (tree != null && tree.ShouldTick())
                    {
                        tree.Tick();
                    }

                    _bucketIndices[priority] = (idx + 1) % count;
                    tickedCount++;
                }
            }
        }

        public void Clear()
        {
            for (int i = 0; i < _priorityLevelCount; i++)
            {
                _buckets[i].Clear();
                _bucketIndices[i] = 0;
            }
        }

        public int GetTreeCount(int priority)
        {
            priority = ClampPriority(priority);
            return _buckets[priority].Count;
        }

        public int GetTotalCount()
        {
            int total = 0;
            for (int i = 0; i < _priorityLevelCount; i++)
            {
                total += _buckets[i].Count;
            }
            return total;
        }

        private int ClampPriority(int priority)
        {
            if (priority < 0) return 0;
            if (priority >= MAX_PRIORITY_LEVELS) return MAX_PRIORITY_LEVELS - 1;
            return priority;
        }
    }
}
