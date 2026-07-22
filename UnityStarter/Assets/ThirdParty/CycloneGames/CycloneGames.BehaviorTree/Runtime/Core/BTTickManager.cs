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
        private const int DEFAULT_CAPACITY = 1024;

        private RuntimeBehaviorTree[] _trees;
        private int _capacity;
        private int _count;
        private int _currentIndex;
        private bool _isTicking;
        private readonly List<PendingMutation> _pendingMutations;
        private readonly int _ownerThreadId;
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

        public BTTickManager(int initialCapacity = DEFAULT_CAPACITY)
        {
            if (initialCapacity < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(initialCapacity),
                    initialCapacity,
                    "Tick manager capacity must be at least 1.");
            }

            _capacity = initialCapacity;
            _ownerThreadId = Environment.CurrentManagedThreadId;
            _trees = new RuntimeBehaviorTree[_capacity];
            _pendingMutations = new List<PendingMutation>(Math.Min(initialCapacity, 64));
            _count = 0;
            _currentIndex = 0;
        }

        public void Register(RuntimeBehaviorTree tree)
        {
            EnsureOwnerThread();
            if (tree == null) return;

            if (_isTicking)
            {
                _pendingMutations.Add(new PendingMutation(tree, true));
                return;
            }

            RegisterImmediate(tree);
        }

        private void RegisterImmediate(RuntimeBehaviorTree tree)
        {
            if (tree == null || tree.IsStopped) return;

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
            EnsureOwnerThread();
            if (tree == null) return;

            if (_isTicking)
            {
                _pendingMutations.Add(new PendingMutation(tree, false));
                return;
            }

            UnregisterImmediate(tree);
        }

        private void UnregisterImmediate(RuntimeBehaviorTree tree)
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
                        _pendingMutations.Add(new PendingMutation(tree, false));
                    }
                    else if (tree.ShouldTick())
                    {
                        RuntimeState state = tree.Tick();
                        if (state == RuntimeState.Success || state == RuntimeState.Failure || tree.IsStopped)
                        {
                            _pendingMutations.Add(new PendingMutation(tree, false));
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
            _pendingMutations.Clear();
        }

        private void ApplyPendingMutations()
        {
            for (int i = 0; i < _pendingMutations.Count; i++)
            {
                PendingMutation mutation = _pendingMutations[i];
                if (mutation.Register)
                {
                    RegisterImmediate(mutation.Tree);
                }
                else
                {
                    UnregisterImmediate(mutation.Tree);
                }
            }
            _pendingMutations.Clear();
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
