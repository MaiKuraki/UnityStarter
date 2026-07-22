using System;
using System.Collections.Generic;

namespace CycloneGames.BehaviorTree.Runtime.Core
{
    /// <summary>
    /// Priority-based, allocation-free steady-state tick manager.
    /// Registration changes requested from callbacks are committed after the current tick pass.
    /// The manager is owned and called by one thread.
    /// </summary>
    public sealed class BTPriorityTickManager
    {
        private const int DEFAULT_CAPACITY = 256;
        private const int MAX_PRIORITY_LEVELS = 8;

        private readonly List<RuntimeBehaviorTree>[] _buckets;
        private readonly int[] _bucketIndices;
        private readonly int[] _budgets;

        private readonly Dictionary<RuntimeBehaviorTree, TreeLocation> _treeLocations;
        private readonly List<PendingMutation> _pendingMutations;
        private readonly int _ownerThreadId;

        private readonly int _priorityLevelCount;
        private bool _isTicking;

        public BTPriorityTickManager(int[] budgets = null)
        {
            _priorityLevelCount = MAX_PRIORITY_LEVELS;
            _ownerThreadId = Environment.CurrentManagedThreadId;
            _buckets = new List<RuntimeBehaviorTree>[MAX_PRIORITY_LEVELS];
            _bucketIndices = new int[MAX_PRIORITY_LEVELS];
            _budgets = new[] { 100, 50, 30, 20, 15, 10, 5, 5 };
            _treeLocations = new Dictionary<RuntimeBehaviorTree, TreeLocation>(DEFAULT_CAPACITY);
            _pendingMutations = new List<PendingMutation>(32);

            for (int i = 0; i < MAX_PRIORITY_LEVELS; i++)
            {
                _buckets[i] = new List<RuntimeBehaviorTree>(DEFAULT_CAPACITY);
                _bucketIndices[i] = 0;
            }

            if (budgets != null)
            {
                SetBudgets(budgets);
            }
        }

        public void SetBudgets(int[] budgets)
        {
            EnsureOwnerThread();
            if (budgets == null)
            {
                throw new ArgumentNullException(nameof(budgets));
            }

            for (int i = 0; i < budgets.Length && i < MAX_PRIORITY_LEVELS; i++)
            {
                if (budgets[i] < 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(budgets),
                        budgets[i],
                        "Priority tick budgets cannot be negative.");
                }
                _budgets[i] = budgets[i];
            }
        }

        public void Register(RuntimeBehaviorTree tree, int priority = 0)
        {
            EnsureOwnerThread();
            if (tree == null) return;
            priority = ClampPriority(priority);

            if (_isTicking)
            {
                _pendingMutations.Add(new PendingMutation(tree, priority, true));
                return;
            }

            RegisterImmediate(tree, priority);
        }

        private void RegisterImmediate(RuntimeBehaviorTree tree, int priority)
        {
            if (tree == null || tree.IsStopped) return;
            priority = ClampPriority(priority);

            if (_treeLocations.TryGetValue(tree, out TreeLocation existing))
            {
                if (existing.Bucket == priority) return;
                RemoveAt(existing);
            }

            List<RuntimeBehaviorTree> bucket = _buckets[priority];
            int index = bucket.Count;
            bucket.Add(tree);
            _treeLocations[tree] = new TreeLocation(priority, index);
        }

        public void Unregister(RuntimeBehaviorTree tree)
        {
            EnsureOwnerThread();
            if (tree == null) return;

            if (_isTicking)
            {
                _pendingMutations.Add(new PendingMutation(tree, 0, false));
                return;
            }

            UnregisterImmediate(tree);
        }

        private void UnregisterImmediate(RuntimeBehaviorTree tree)
        {
            if (tree != null && _treeLocations.TryGetValue(tree, out TreeLocation location))
            {
                RemoveAt(location);
            }
        }

        public void UpdatePriority(RuntimeBehaviorTree tree, int newPriority)
        {
            EnsureOwnerThread();
            Register(tree, newPriority);
        }

        private void RemoveAt(TreeLocation location)
        {
            List<RuntimeBehaviorTree> bucket = _buckets[location.Bucket];
            int last = bucket.Count - 1;
            if (location.Index < 0 || location.Index > last)
            {
                return;
            }

            RuntimeBehaviorTree removed = bucket[location.Index];
            RuntimeBehaviorTree moved = bucket[last];
            bucket[location.Index] = moved;
            bucket.RemoveAt(last);
            _treeLocations.Remove(removed);

            if (location.Index < bucket.Count)
            {
                _treeLocations[moved] = new TreeLocation(location.Bucket, location.Index);
            }

            if (_bucketIndices[location.Bucket] >= bucket.Count)
            {
                _bucketIndices[location.Bucket] = 0;
            }
        }

        public void Tick()
        {
            EnsureOwnerThread();
            if (_isTicking)
            {
                throw new InvalidOperationException("Priority tick manager cannot be ticked reentrantly.");
            }

            _isTicking = true;
            try
            {
                for (int priority = 0; priority < _priorityLevelCount; priority++)
                {
                    List<RuntimeBehaviorTree> bucket = _buckets[priority];
                    int snapshotCount = bucket.Count;
                    if (snapshotCount == 0) continue;

                    int budget = _budgets[priority];
                    int scannedCount = 0;
                    while (scannedCount < budget && scannedCount < snapshotCount)
                    {
                        int index = _bucketIndices[priority];
                        if (index >= bucket.Count)
                        {
                            index = 0;
                        }

                        RuntimeBehaviorTree tree = bucket[index];
                        if (tree == null || tree.IsStopped)
                        {
                            _pendingMutations.Add(new PendingMutation(tree, 0, false));
                        }
                        else if (tree.ShouldTick())
                        {
                            RuntimeState state = tree.Tick();
                            if (state == RuntimeState.Success || state == RuntimeState.Failure || tree.IsStopped)
                            {
                                _pendingMutations.Add(new PendingMutation(tree, 0, false));
                            }
                        }

                        _bucketIndices[priority] = (index + 1) % snapshotCount;
                        scannedCount++;
                    }
                }
            }
            finally
            {
                _isTicking = false;
                ApplyPendingMutations();
            }
        }

        public void Clear()
        {
            EnsureOwnerThread();
            if (_isTicking)
            {
                throw new InvalidOperationException("Priority tick manager cannot be cleared during a tick pass.");
            }

            for (int i = 0; i < _priorityLevelCount; i++)
            {
                _buckets[i].Clear();
                _bucketIndices[i] = 0;
            }
            _treeLocations.Clear();
            _pendingMutations.Clear();
        }

        public int GetTreeCount(int priority)
        {
            EnsureOwnerThread();
            priority = ClampPriority(priority);
            return _buckets[priority].Count;
        }

        public int GetTotalCount()
        {
            EnsureOwnerThread();
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

        private void ApplyPendingMutations()
        {
            for (int i = 0; i < _pendingMutations.Count; i++)
            {
                PendingMutation mutation = _pendingMutations[i];
                if (mutation.Register)
                {
                    RegisterImmediate(mutation.Tree, mutation.Priority);
                }
                else
                {
                    UnregisterImmediate(mutation.Tree);
                }
            }
            _pendingMutations.Clear();
        }

        private readonly struct TreeLocation
        {
            public readonly int Bucket;
            public readonly int Index;

            public TreeLocation(int bucket, int index)
            {
                Bucket = bucket;
                Index = index;
            }
        }

        private readonly struct PendingMutation
        {
            public readonly RuntimeBehaviorTree Tree;
            public readonly int Priority;
            public readonly bool Register;

            public PendingMutation(RuntimeBehaviorTree tree, int priority, bool register)
            {
                Tree = tree;
                Priority = priority;
                Register = register;
            }
        }

        private void EnsureOwnerThread()
        {
            if (Environment.CurrentManagedThreadId != _ownerThreadId)
            {
                throw new InvalidOperationException(
                    $"BTPriorityTickManager must run on owner thread {_ownerThreadId}.");
            }
        }
    }
}
