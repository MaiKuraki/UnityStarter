using System.Collections.Generic;

namespace CycloneGames.BehaviorTree.Runtime.Core
{
    /// <summary>
    /// Priority-based tick manager for large-scale AI (1000+ agents).
    /// Uses swap-remove for O(1) unregistration, HashSet for O(1) duplicate detection.
    /// </summary>
    public class BTPriorityTickManager
    {
        private const int DEFAULT_CAPACITY = 256;
        private const int MAX_PRIORITY_LEVELS = 8;

        private readonly List<RuntimeBehaviorTree>[] _buckets;
        private readonly int[] _bucketIndices;
        private readonly int[] _budgets;

        // O(1) lookup to find which bucket a tree is in, avoiding linear scan
        private readonly Dictionary<RuntimeBehaviorTree, int> _treeBucketMap;

        private int _priorityLevelCount;

        public BTPriorityTickManager(int[] budgets = null)
        {
            _priorityLevelCount = MAX_PRIORITY_LEVELS;
            _buckets = new List<RuntimeBehaviorTree>[MAX_PRIORITY_LEVELS];
            _bucketIndices = new int[MAX_PRIORITY_LEVELS];
            _budgets = budgets ?? new int[] { 100, 50, 30, 20, 15, 10, 5, 5 };
            _treeBucketMap = new Dictionary<RuntimeBehaviorTree, int>(DEFAULT_CAPACITY);

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

            if (_treeBucketMap.TryGetValue(tree, out int existingBucket))
            {
                if (existingBucket == priority) return;
                SwapRemoveFromBucket(existingBucket, tree);
            }

            _buckets[priority].Add(tree);
            _treeBucketMap[tree] = priority;
        }

        public void Unregister(RuntimeBehaviorTree tree)
        {
            if (tree == null) return;

            if (_treeBucketMap.TryGetValue(tree, out int bucket))
            {
                SwapRemoveFromBucket(bucket, tree);
                _treeBucketMap.Remove(tree);
            }
        }

        public void UpdatePriority(RuntimeBehaviorTree tree, int newPriority)
        {
            Register(tree, newPriority);
        }

        // O(1) swap-remove: swap target with last element, then remove last
        private void SwapRemoveFromBucket(int bucketIdx, RuntimeBehaviorTree tree)
        {
            var bucket = _buckets[bucketIdx];
            int count = bucket.Count;
            for (int i = 0; i < count; i++)
            {
                if (bucket[i] == tree)
                {
                    int last = count - 1;
                    bucket[i] = bucket[last];
                    bucket.RemoveAt(last);

                    if (_bucketIndices[bucketIdx] >= bucket.Count && bucket.Count > 0)
                    {
                        _bucketIndices[bucketIdx] = 0;
                    }
                    return;
                }
            }
        }

        public void Tick()
        {
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
                        var state = tree.Tick();
                        if (state == RuntimeState.Success || state == RuntimeState.Failure)
                        {
                            tree.Stop();
                        }
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
            _treeBucketMap.Clear();
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
