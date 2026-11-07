using System;
using System.Collections.Generic;

namespace CycloneGames.BehaviorTree.Runtime.Core
{
    /// <summary>
    /// Allocation-free steady-state tick manager with bounded round-robin work.
    /// Registration changes requested from callbacks are committed after the current tick pass.
    /// The manager is owned and called by one thread.
    /// </summary>
    public sealed class BTTickManager
    {

        private RuntimeBehaviorTree[] _trees;
        private readonly Dictionary<RuntimeBehaviorTree, int> _treeIndices;
        private int _capacity;
        private int _count;
        private int _currentIndex;
        private bool _isTicking;
        private readonly List<PendingMutation> _pendingMutations;
        private readonly Dictionary<RuntimeBehaviorTree, int> _pendingMutationIndices;
        private readonly int _ownerThreadId;
        private readonly int _maximumTreeCount;
        private readonly int _maximumPendingMutationCount;
        private int _peakTreeCount;
        private int _peakPendingMutationCount;
        private long _capacityRejectedTreeCount;
        private long _capacityRejectedMutationCount;
        private int _tickBudget = 100;

        public int TickBudget
        {
            get => _tickBudget;
            set
            {
                EnsureOwnerThread();
                if (value < 1)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, "Tick budget must be at least 1.");
                }
                _tickBudget = value;
            }
        }
        public int Count
        {
            get
            {
                EnsureOwnerThread();
                return _count;
            }
        }

        public BTTickManager(int initialCapacity = BTCapacityLimits.DefaultTickManagerCapacity)
            : this(initialCapacity, BTCapacityLimits.DefaultAgentCount, BTCapacityLimits.DefaultAgentCount)
        {
        }

        public BTTickManager(
            int initialCapacity,
            int maximumTreeCount,
            int maximumPendingMutationCount)
        {
            if (initialCapacity < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(initialCapacity),
                    initialCapacity,
                    "Tick manager capacity must be at least 1.");
            }

            if (maximumTreeCount < initialCapacity || maximumTreeCount > BTCapacityLimits.MaxAgentCount)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumTreeCount));
            }

            if (maximumPendingMutationCount < 1 ||
                maximumPendingMutationCount > BTCapacityLimits.MaxAgentCount)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumPendingMutationCount));
            }

            _capacity = initialCapacity;
            _maximumTreeCount = maximumTreeCount;
            _maximumPendingMutationCount = maximumPendingMutationCount;
            _ownerThreadId = Environment.CurrentManagedThreadId;
            _trees = new RuntimeBehaviorTree[_capacity];
            _treeIndices = new Dictionary<RuntimeBehaviorTree, int>(_capacity);
            int pendingInitialCapacity = Math.Min(maximumPendingMutationCount, 64);
            _pendingMutations = new List<PendingMutation>(pendingInitialCapacity);
            _pendingMutationIndices = new Dictionary<RuntimeBehaviorTree, int>(pendingInitialCapacity);
            _count = 0;
            _currentIndex = 0;
        }

        public void Register(RuntimeBehaviorTree tree)
        {
            TryRegister(tree);
        }

        public bool TryRegister(RuntimeBehaviorTree tree)
        {
            EnsureOwnerThread();
            if (tree == null || tree.IsStopped) return false;

            if (_isTicking)
            {
                return TryQueueMutation(tree, true);
            }

            return TryRegisterImmediate(tree);
        }

        private bool TryRegisterImmediate(RuntimeBehaviorTree tree)
        {
            if (tree == null || tree.IsStopped) return false;

            if (_treeIndices.ContainsKey(tree)) return true;

            if (_count >= _maximumTreeCount)
            {
                _capacityRejectedTreeCount++;
                return false;
            }

            if (_count >= _capacity)
            {
                int newCapacity = Math.Min(
                    _maximumTreeCount,
                    _capacity <= _maximumTreeCount / 2 ? _capacity * 2 : _maximumTreeCount);
                var newArray = new RuntimeBehaviorTree[newCapacity];
                for (int i = 0; i < _count; i++)
                {
                    newArray[i] = _trees[i];
                }
                _trees = newArray;
                _capacity = newCapacity;
            }

            _trees[_count] = tree;
            _treeIndices[tree] = _count;
            _count++;
            if (_count > _peakTreeCount)
            {
                _peakTreeCount = _count;
            }

            return true;
        }

        public void Unregister(RuntimeBehaviorTree tree)
        {
            TryUnregister(tree);
        }

        public bool TryUnregister(RuntimeBehaviorTree tree)
        {
            EnsureOwnerThread();
            if (tree == null) return false;

            if (_isTicking)
            {
                return TryQueueMutation(tree, false);
            }

            return UnregisterImmediate(tree);
        }

        private bool UnregisterImmediate(RuntimeBehaviorTree tree)
        {
            if (tree == null) return false;

            if (!_treeIndices.TryGetValue(tree, out int index))
            {
                return false;
            }

            _count--;
            RuntimeBehaviorTree moved = _trees[_count];
            _trees[index] = moved;
            _trees[_count] = null;
            _treeIndices.Remove(tree);
            if (index < _count && moved != null)
            {
                _treeIndices[moved] = index;
            }

            if (_currentIndex >= _count && _count > 0)
            {
                _currentIndex = 0;
            }
            return true;
        }

        public void Tick()
        {
            EnsureOwnerThread();
            if (_isTicking)
            {
                throw new InvalidOperationException("Tick manager cannot be ticked reentrantly.");
            }

            if (_count == 0) return;

            int scannedCount = 0;
            int budget = _tickBudget;
            int snapshotCount = _count;
            _isTicking = true;

            try
            {
                while (scannedCount < budget && scannedCount < snapshotCount && _count > 0)
                {
                    if (_currentIndex >= _count)
                    {
                        _currentIndex = 0;
                    }

                    RuntimeBehaviorTree tree = _trees[_currentIndex];
                    if (tree == null || tree.IsStopped)
                    {
                        TryQueueMutation(tree, false);
                    }
                    else if (tree.ShouldTick())
                    {
                        RuntimeState state = tree.Tick();
                        if (state == RuntimeState.Success || state == RuntimeState.Failure || tree.IsStopped)
                        {
                            TryQueueMutation(tree, false);
                        }
                    }

                    _currentIndex++;
                    scannedCount++;
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
                throw new InvalidOperationException("Tick manager cannot be cleared during a tick pass.");
            }

            for (int i = 0; i < _count; i++)
            {
                _trees[i] = null;
            }
            _count = 0;
            _currentIndex = 0;
            _treeIndices.Clear();
            _pendingMutations.Clear();
            _pendingMutationIndices.Clear();
        }

        public BTTickManagerMemoryStats GetMemoryStats()
        {
            EnsureOwnerThread();
            return new BTTickManagerMemoryStats(
                _count,
                _capacity,
                _maximumTreeCount,
                _peakTreeCount,
                _pendingMutations.Count,
                _maximumPendingMutationCount,
                _peakPendingMutationCount,
                _capacityRejectedTreeCount,
                _capacityRejectedMutationCount);
        }

        private bool TryQueueMutation(RuntimeBehaviorTree tree, bool register)
        {
            if (tree == null)
            {
                return false;
            }

            if (_pendingMutationIndices.TryGetValue(tree, out int existingIndex))
            {
                _pendingMutations[existingIndex] = new PendingMutation(tree, register);
                return true;
            }

            if (_pendingMutations.Count >= _maximumPendingMutationCount)
            {
                _capacityRejectedMutationCount++;
                return false;
            }

            int index = _pendingMutations.Count;
            _pendingMutations.Add(new PendingMutation(tree, register));
            _pendingMutationIndices.Add(tree, index);
            if (_pendingMutations.Count > _peakPendingMutationCount)
            {
                _peakPendingMutationCount = _pendingMutations.Count;
            }

            return true;
        }

        private void ApplyPendingMutations()
        {
            for (int i = 0; i < _pendingMutations.Count; i++)
            {
                PendingMutation mutation = _pendingMutations[i];
                if (mutation.Register)
                {
                    TryRegisterImmediate(mutation.Tree);
                }
                else
                {
                    UnregisterImmediate(mutation.Tree);
                }
            }
            _pendingMutations.Clear();
            _pendingMutationIndices.Clear();
        }

        private readonly struct PendingMutation
        {
            public readonly RuntimeBehaviorTree Tree;
            public readonly bool Register;

            public PendingMutation(RuntimeBehaviorTree tree, bool register)
            {
                Tree = tree;
                Register = register;
            }
        }

        private void EnsureOwnerThread()
        {
            if (Environment.CurrentManagedThreadId != _ownerThreadId)
            {
                throw new InvalidOperationException(
                    $"BTTickManager must run on owner thread {_ownerThreadId}.");
            }
        }
    }
}
