using System.Collections.Generic;

namespace CycloneGames.RPGFoundation.Interaction.Core
{
    public sealed class InteractionRateLimiter
    {
        /// <summary>Implementation safety ceiling for retained instigator windows.</summary>
        public const int MaximumWindowCount = 65_536;

        /// <summary>Maximum expired-window removals performed by one request.</summary>
        public const int MaximumExpiredWindowsToPrunePerCall = 256;

        private readonly Dictionary<ulong, WindowState> _windows = new Dictionary<ulong, WindowState>();
        private readonly List<ulong> _expiryHeap = new List<ulong>();
        private readonly int _maximumWindowCount;
        private long _rejectedWindowAdmissionCount;
        private long _expiredWindowRemovalCount;
        private long _explicitWindowRemovalCount;
        private int _lastObservedTick;
        private long _monotonicTick;
        private bool _hasObservedTick;

        public InteractionRateLimiter()
            : this(MaximumWindowCount)
        {
        }

        /// <summary>Creates one owner with a product-specific capacity below the implementation ceiling.</summary>
        public InteractionRateLimiter(int maximumWindowCount)
        {
            if (maximumWindowCount <= 0 || maximumWindowCount > MaximumWindowCount)
            {
                throw new System.ArgumentOutOfRangeException(nameof(maximumWindowCount));
            }

            _maximumWindowCount = maximumWindowCount;
        }

        internal int Count => _windows.Count;
        internal long RejectedWindowAdmissionCount => _rejectedWindowAdmissionCount;

        /// <summary>Returns an allocation-free O(1) admission snapshot.</summary>
        public InteractionRateLimiterMemorySnapshot GetMemorySnapshot()
        {
            return new InteractionRateLimiterMemorySnapshot(
                _windows.Count,
                _maximumWindowCount,
                _rejectedWindowAdmissionCount,
                _expiredWindowRemovalCount,
                _explicitWindowRemovalCount);
        }

        public bool TryConsume(ulong key, int tick, int maxRequests, int windowTicks)
        {
            if (key == InteractionStableId.None || maxRequests <= 0 || windowTicks <= 0)
            {
                return true;
            }

            long monotonicTick = ObserveTick(tick);
            PruneExpired(monotonicTick, MaximumExpiredWindowsToPrunePerCall);

            if (!_windows.TryGetValue(key, out WindowState state))
            {
                if (_windows.Count >= _maximumWindowCount)
                {
                    if (_rejectedWindowAdmissionCount < long.MaxValue)
                    {
                        _rejectedWindowAdmissionCount++;
                    }

                    return false;
                }

                AddWindow(key, monotonicTick, windowTicks);
                return true;
            }

            long expiryTick = CalculateExpiryTick(state.WindowStartTick, windowTicks);
            if (monotonicTick >= expiryTick)
            {
                ResetWindow(key, state, monotonicTick, windowTicks);
                return true;
            }

            if (state.ExpiryTick != expiryTick)
            {
                state.ExpiryTick = expiryTick;
                _windows[key] = state;
                RestoreHeapAt(state.HeapIndex);
            }

            if (state.Count >= maxRequests)
            {
                return false;
            }

            state.Count++;
            _windows[key] = state;
            return true;
        }

        /// <summary>Removes one instigator window, for example when its authenticated session disconnects.</summary>
        public bool Remove(ulong key)
        {
            if (!_windows.TryGetValue(key, out WindowState state))
            {
                return false;
            }

            RemoveHeapEntry(state.HeapIndex);
            if (_explicitWindowRemovalCount < long.MaxValue)
            {
                _explicitWindowRemovalCount++;
            }

            return true;
        }

        public void Clear()
        {
            _windows.Clear();
            _expiryHeap.Clear();
            _hasObservedTick = false;
            _monotonicTick = 0L;
        }

        private long ObserveTick(int tick)
        {
            if (!_hasObservedTick)
            {
                _lastObservedTick = tick;
                _monotonicTick = 0L;
                _hasObservedTick = true;
                return _monotonicTick;
            }

            int delta = unchecked(tick - _lastObservedTick);
            if (delta > 0)
            {
                _lastObservedTick = tick;
                _monotonicTick = SaturatingAdd(_monotonicTick, delta);
            }

            return _monotonicTick;
        }

        private void PruneExpired(long tick, int maximumRemovals)
        {
            int removed = 0;
            while (removed < maximumRemovals && _expiryHeap.Count > 0)
            {
                ulong key = _expiryHeap[0];
                if (!_windows.TryGetValue(key, out WindowState state))
                {
                    RemoveHeapEntry(0);
                    continue;
                }

                if (state.ExpiryTick > tick)
                {
                    break;
                }

                RemoveHeapEntry(0);
                removed++;
            }

            if (removed > 0)
            {
                long remaining = long.MaxValue - _expiredWindowRemovalCount;
                _expiredWindowRemovalCount += removed > remaining ? remaining : removed;
            }
        }

        private void AddWindow(ulong key, long tick, int windowTicks)
        {
            var state = new WindowState(
                tick,
                1,
                CalculateExpiryTick(tick, windowTicks),
                _expiryHeap.Count);
            _windows.Add(key, state);
            _expiryHeap.Add(key);
            SiftUp(state.HeapIndex);
        }

        private void ResetWindow(ulong key, WindowState state, long tick, int windowTicks)
        {
            state.WindowStartTick = tick;
            state.Count = 1;
            state.ExpiryTick = CalculateExpiryTick(tick, windowTicks);
            _windows[key] = state;
            RestoreHeapAt(state.HeapIndex);
        }

        private void RemoveHeapEntry(int index)
        {
            int lastIndex = _expiryHeap.Count - 1;
            if ((uint)index > (uint)lastIndex)
            {
                return;
            }

            ulong removedKey = _expiryHeap[index];
            if (index != lastIndex)
            {
                ulong movedKey = _expiryHeap[lastIndex];
                _expiryHeap[index] = movedKey;
                WindowState movedState = _windows[movedKey];
                movedState.HeapIndex = index;
                _windows[movedKey] = movedState;
            }

            _expiryHeap.RemoveAt(lastIndex);
            _windows.Remove(removedKey);
            if (index < _expiryHeap.Count)
            {
                RestoreHeapAt(index);
            }
        }

        private void RestoreHeapAt(int index)
        {
            if (index > 0 && CompareHeapEntries(index, (index - 1) / 2) < 0)
            {
                SiftUp(index);
            }
            else
            {
                SiftDown(index);
            }
        }

        private void SiftUp(int index)
        {
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (CompareHeapEntries(index, parent) >= 0)
                {
                    break;
                }

                SwapHeapEntries(index, parent);
                index = parent;
            }
        }

        private void SiftDown(int index)
        {
            while (true)
            {
                int left = (index * 2) + 1;
                if (left >= _expiryHeap.Count)
                {
                    return;
                }

                int right = left + 1;
                int smallest = right < _expiryHeap.Count && CompareHeapEntries(right, left) < 0
                    ? right
                    : left;
                if (CompareHeapEntries(smallest, index) >= 0)
                {
                    return;
                }

                SwapHeapEntries(index, smallest);
                index = smallest;
            }
        }

        private int CompareHeapEntries(int leftIndex, int rightIndex)
        {
            ulong leftKey = _expiryHeap[leftIndex];
            ulong rightKey = _expiryHeap[rightIndex];
            long leftExpiry = _windows[leftKey].ExpiryTick;
            long rightExpiry = _windows[rightKey].ExpiryTick;
            int expiryComparison = leftExpiry.CompareTo(rightExpiry);
            return expiryComparison != 0 ? expiryComparison : leftKey.CompareTo(rightKey);
        }

        private void SwapHeapEntries(int leftIndex, int rightIndex)
        {
            ulong leftKey = _expiryHeap[leftIndex];
            ulong rightKey = _expiryHeap[rightIndex];
            _expiryHeap[leftIndex] = rightKey;
            _expiryHeap[rightIndex] = leftKey;

            WindowState leftState = _windows[leftKey];
            leftState.HeapIndex = rightIndex;
            _windows[leftKey] = leftState;

            WindowState rightState = _windows[rightKey];
            rightState.HeapIndex = leftIndex;
            _windows[rightKey] = rightState;
        }

        private static long CalculateExpiryTick(long windowStartTick, int windowTicks)
        {
            return SaturatingAdd(windowStartTick, windowTicks);
        }

        private static long SaturatingAdd(long value, int positiveDelta)
        {
            return value > long.MaxValue - positiveDelta
                ? long.MaxValue
                : value + positiveDelta;
        }

        private struct WindowState
        {
            public long WindowStartTick;
            public int Count;
            public long ExpiryTick;
            public int HeapIndex;

            public WindowState(long windowStartTick, int count, long expiryTick, int heapIndex)
            {
                WindowStartTick = windowStartTick;
                Count = count;
                ExpiryTick = expiryTick;
                HeapIndex = heapIndex;
            }
        }
    }
}
