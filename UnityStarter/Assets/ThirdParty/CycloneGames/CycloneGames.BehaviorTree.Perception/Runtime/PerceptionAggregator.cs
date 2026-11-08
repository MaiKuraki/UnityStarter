using System;
using CycloneGames.AIPerception.Runtime;
using CycloneGames.BehaviorTree.Core;

namespace CycloneGames.BehaviorTree.Runtime.Integrations.Perception
{
    /// <summary>Merges the targets of several bridges into one ordered view.</summary>
    /// <remarks>
    /// Merge rules, applied in this order:
    /// <list type="number">
    /// <item>Targets are identified by <see cref="PerceptibleHandle"/>; one handle yields one entry.</item>
    /// <item>A live detection wins over a remembered one.</item>
    /// <item>Between two remembered entries, the nearer one wins.</item>
    /// <item>Entries are ordered by distance, ascending.</item>
    /// <item>When the capacity is reached, the nearest entries are kept and <see cref="IsTruncated"/> is set.</item>
    /// </list>
    /// Buffers are allocated in the constructor; update and publish allocate nothing. One aggregator
    /// belongs to one thread and calls <see cref="PerceptionBlackboardBridge.Update"/> on every
    /// registered bridge.
    /// </remarks>
    public sealed class PerceptionAggregator : IDisposable
    {
        private static readonly PerceptionBlackboardBridge[] NoBridges = Array.Empty<PerceptionBlackboardBridge>();
        private static readonly PerceptionTarget[] NoTargets = Array.Empty<PerceptionTarget>();

        private readonly int _ownerThreadId;
        private PerceptionBlackboardBridge[] _bridges;
        private PerceptionTarget[] _merged;
        private PerceptionTarget[] _staging;
        private int _bridgeCount;
        private int _count;
        private bool _truncated;
        private bool _disposed;

        /// <param name="capacity">Upper bound on merged targets.</param>
        /// <param name="bridgeCapacity">Upper bound on registered bridges.</param>
        public PerceptionAggregator(int capacity = 8, int bridgeCapacity = 4)
        {
            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "At least one target must be kept.");
            }

            if (bridgeCapacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(bridgeCapacity), bridgeCapacity, "At least one bridge must be allowed.");
            }

            _merged = new PerceptionTarget[capacity];
            _staging = new PerceptionTarget[capacity];
            _bridges = new PerceptionBlackboardBridge[bridgeCapacity];
            _ownerThreadId = Environment.CurrentManagedThreadId;
        }

        public int Capacity => _merged.Length;

        /// <summary>Merged targets of the last update, nearest first.</summary>
        public int Count => _count;

        public bool IsTruncated => _truncated;

        public PerceptionTarget Nearest => _count > 0 ? _merged[0] : default;

        public ReadOnlySpan<PerceptionTarget> Targets => new ReadOnlySpan<PerceptionTarget>(_merged, 0, _count);

        /// <summary>
        /// Registers a bridge. Returns false for a null bridge, a bridge already registered, or a full
        /// aggregator. The bridge stays owned by the caller.
        /// </summary>
        public bool Add(PerceptionBlackboardBridge bridge)
        {
            ThrowIfDisposed();
            EnsureOwnerThread();
            if (bridge == null || _bridgeCount >= _bridges.Length)
            {
                return false;
            }

            for (int i = 0; i < _bridgeCount; i++)
            {
                if (ReferenceEquals(_bridges[i], bridge))
                {
                    return false;
                }
            }

            _bridges[_bridgeCount++] = bridge;

            // Sized to the sum of bridge capacities. Grown only here, never during Update.
            int required = 0;
            for (int i = 0; i < _bridgeCount; i++)
            {
                required += _bridges[i].Capacity;
            }

            if (_staging.Length < required)
            {
                _staging = new PerceptionTarget[required];
            }

            return true;
        }

        /// <summary>
        /// Updates every registered bridge and merges the results.
        /// </summary>
        /// <remarks>
        /// Three passes: collect every target, collapse duplicates per handle, keep the nearest
        /// <see cref="Capacity"/> entries. The outcome does not depend on bridge order.
        /// </remarks>
        /// <returns>True when at least one target survived the merge.</returns>
        public bool Update()
        {
            ThrowIfDisposed();
            EnsureOwnerThread();

            int staged = 0;
            for (int bridgeIndex = 0; bridgeIndex < _bridgeCount; bridgeIndex++)
            {
                PerceptionBlackboardBridge bridge = _bridges[bridgeIndex];
                if (!bridge.Update())
                {
                    continue;
                }

                ReadOnlySpan<PerceptionTarget> targets = bridge.Targets;
                for (int targetIndex = 0; targetIndex < targets.Length && staged < _staging.Length; targetIndex++)
                {
                    _staging[staged++] = targets[targetIndex];
                }
            }

            int distinct = CollapseDuplicates(staged);
            SortByDistance(distinct);

            int keep = Math.Min(distinct, _merged.Length);
            for (int i = 0; i < keep; i++)
            {
                _merged[i] = _staging[i];
            }

            _count = keep;
            _truncated = distinct > keep;
            return _count > 0;
        }

        public void PublishNearest(RuntimeBlackboard blackboard, int visibleKey, int distanceKey)
        {
            ThrowIfDisposed();
            EnsureOwnerThread();
            if (blackboard == null)
            {
                throw new ArgumentNullException(nameof(blackboard));
            }

            blackboard.SetBool(visibleKey, _count > 0);
            blackboard.SetFloat(distanceKey, _count > 0 ? _merged[0].Distance : 0f);
        }

        public void PublishNearestPosition(RuntimeBlackboard blackboard, int positionKey)
        {
            ThrowIfDisposed();
            EnsureOwnerThread();
            if (blackboard == null)
            {
                throw new ArgumentNullException(nameof(blackboard));
            }

            var position = _count > 0 ? _merged[0].Position : default;
            blackboard.SetFloat3(positionKey, new BTFloat3(position.x, position.y, position.z));
        }

        public void PublishNearestHandle(RuntimeBlackboard blackboard, int handleKey)
        {
            ThrowIfDisposed();
            EnsureOwnerThread();
            if (blackboard == null)
            {
                throw new ArgumentNullException(nameof(blackboard));
            }

            blackboard.SetLong3(handleKey, PerceptionBlackboardBridge.PackHandle(_count > 0 ? _merged[0].Handle : default));
        }

        public void PublishNearestFromMemory(RuntimeBlackboard blackboard, int fromMemoryKey)
        {
            ThrowIfDisposed();
            EnsureOwnerThread();
            if (blackboard == null)
            {
                throw new ArgumentNullException(nameof(blackboard));
            }

            blackboard.SetBool(fromMemoryKey, _count > 0 && _merged[0].IsFromMemory);
        }

        public void PublishCount(RuntimeBlackboard blackboard, int countKey)
        {
            ThrowIfDisposed();
            EnsureOwnerThread();
            if (blackboard == null)
            {
                throw new ArgumentNullException(nameof(blackboard));
            }

            blackboard.SetInt(countKey, _count);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _count = 0;
            _bridgeCount = 0;
            _truncated = false;
            _merged = NoTargets;
            _staging = NoTargets;
            _bridges = NoBridges;
        }

        /// <summary>Collapses duplicates in the staged prefix, keeping the better entry per handle.</summary>
        /// <returns>Number of distinct handles.</returns>
        private int CollapseDuplicates(int staged)
        {
            int distinct = 0;
            for (int i = 0; i < staged; i++)
            {
                PerceptionTarget candidate = _staging[i];
                int existing = -1;
                for (int j = 0; j < distinct; j++)
                {
                    if (_staging[j].Handle == candidate.Handle)
                    {
                        existing = j;
                        break;
                    }
                }

                if (existing < 0)
                {
                    _staging[distinct++] = candidate;
                    continue;
                }

                if (IsBetter(candidate, _staging[existing]))
                {
                    _staging[existing] = candidate;
                }
            }

            return distinct;
        }

        private static bool IsBetter(in PerceptionTarget candidate, in PerceptionTarget current)
        {
            if (candidate.IsFromMemory != current.IsFromMemory)
            {
                return !candidate.IsFromMemory;
            }

            return candidate.Distance < current.Distance;
        }

        private void SortByDistance(int count)
        {
            for (int i = 1; i < count; i++)
            {
                PerceptionTarget key = _staging[i];
                int j = i - 1;
                while (j >= 0 && _staging[j].Distance > key.Distance)
                {
                    _staging[j + 1] = _staging[j];
                    j--;
                }

                _staging[j + 1] = key;
            }
        }

        private void EnsureOwnerThread()
        {
            if (Environment.CurrentManagedThreadId != _ownerThreadId)
            {
                throw new InvalidOperationException(
                    "PerceptionAggregator must be used from the thread that created it; it pulls bridge results.");
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(PerceptionAggregator));
            }
        }
    }
}
