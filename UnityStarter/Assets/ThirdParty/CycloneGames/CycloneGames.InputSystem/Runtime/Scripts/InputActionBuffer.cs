using System;
using System.Runtime.InteropServices;

namespace CycloneGames.InputSystem.Runtime
{
    /// <summary>
    /// Bounded ring of press edges, each stamped with a tick supplied by the owner. A consumer asks for an edge
    /// inside a window of recent ticks and takes ownership of it, so one press cannot be consumed twice.
    /// </summary>
    /// <remarks>
    /// Ticks must be non-decreasing across calls. The ring keeps insertion order, so a query can stop at the first
    /// entry outside the window: O(1) on a hit, O(capacity) on a miss. Recording is O(1) amortized and allocates
    /// nothing after construction. The owner clears the ring when the active context set changes, so an edge never
    /// crosses an input state.
    /// </remarks>
    public sealed class InputActionBuffer
    {
        /// <summary>Capacity used when the constructor receives none.</summary>
        public static readonly int DefaultCapacity = 64;

        /// <summary>Largest capacity the constructor accepts.</summary>
        public static readonly int MaximumCapacity = 4096;

        /// <summary>Default age, in ticks, at which a pending edge is discarded. 600 ticks is ten seconds at 60 fps.</summary>
        public static readonly int DefaultRetentionTicks = 600;

        private const int UnsetActionId = 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct Entry
        {
            public long Tick;
            public int ActionId;
            public byte Consumed;
        }

        private readonly Entry[] _entries;
        private readonly int _capacity;
        private readonly int _retentionTicks;

        private int _head;
        private int _count;
        private long _recorded;
        private long _dropped;

        /// <summary>Creates a buffer with <see cref="DefaultCapacity"/> and <see cref="DefaultRetentionTicks"/>.</summary>
        public InputActionBuffer()
            : this(DefaultCapacity, DefaultRetentionTicks)
        {
        }

        /// <summary>Creates a buffer with an explicit capacity and retention window.</summary>
        /// <param name="capacity">Number of simultaneous pending edges the ring can hold.</param>
        /// <param name="retentionTicks">Age at which a pending edge is discarded.</param>
        public InputActionBuffer(int capacity, int retentionTicks)
        {
            if (capacity < 1 || capacity > MaximumCapacity)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity is outside the supported range.");
            }

            if (retentionTicks < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(retentionTicks), retentionTicks, "Retention must be positive.");
            }

            _capacity = capacity;
            _retentionTicks = retentionTicks;
            _entries = new Entry[capacity];
        }

        /// <summary>Number of entries the ring can hold.</summary>
        public int Capacity => _capacity;

        /// <summary>Number of entries currently stored, including already consumed ones.</summary>
        public int Count => _count;

        /// <summary>Total edges written since construction.</summary>
        public long RecordedCount => _recorded;

        /// <summary>Total edges discarded because the ring was full when they arrived.</summary>
        public long DroppedCount => _dropped;

        /// <summary>
        /// Appends a press edge. Writing when the ring is full discards the oldest entry, which is the one least
        /// likely to still be inside a consumer window.
        /// </summary>
        /// <param name="actionId">Deterministic action identity; zero is reserved for "unset" and is ignored.</param>
        /// <param name="tick">Owner tick, non-decreasing across calls.</param>
        /// <returns>True when the edge was stored.</returns>
        public bool Record(int actionId, long tick)
        {
            if (actionId == UnsetActionId) return false;

            PruneExpired(tick);

            if (_count == _capacity)
            {
                _head = _head + 1 == _capacity ? 0 : _head + 1;
                _count--;
                _dropped++;
            }

            ref Entry entry = ref _entries[Index(_count)];
            entry.Tick = tick;
            entry.ActionId = actionId;
            entry.Consumed = 0;
            _count++;
            _recorded++;
            return true;
        }

        /// <summary>Takes the newest unconsumed edge inside the window and marks it consumed.</summary>
        /// <param name="actionId">Deterministic action identity.</param>
        /// <param name="currentTick">Owner tick of the query.</param>
        /// <param name="windowTicks">Maximum accepted age, inclusive. Zero accepts only edges of the current tick.</param>
        /// <returns>True when an edge was taken.</returns>
        public bool TryConsume(int actionId, long currentTick, int windowTicks)
        {
            return TryTake(actionId, currentTick, windowTicks, consume: true);
        }

        /// <summary>Reads the newest unconsumed edge inside the window without taking ownership.</summary>
        /// <param name="actionId">Deterministic action identity.</param>
        /// <param name="currentTick">Owner tick of the query.</param>
        /// <param name="windowTicks">Maximum accepted age, inclusive.</param>
        /// <returns>True when an edge is available.</returns>
        public bool TryPeek(int actionId, long currentTick, int windowTicks)
        {
            return TryTake(actionId, currentTick, windowTicks, consume: false);
        }

        /// <summary>Discards every pending edge of one action and returns how many were discarded.</summary>
        /// <param name="actionId">Deterministic action identity.</param>
        public int Clear(int actionId)
        {
            if (actionId == UnsetActionId) return 0;

            int cleared = 0;
            for (int offset = 0; offset < _count; offset++)
            {
                ref Entry entry = ref _entries[Index(offset)];
                if (entry.ActionId != actionId || entry.Consumed != 0) continue;
                entry.Consumed = 1;
                cleared++;
            }

            return cleared;
        }

        /// <summary>Discards every pending edge in O(1). Slots are overwritten in place by later records.</summary>
        public void Clear()
        {
            _head = 0;
            _count = 0;
        }

        private bool TryTake(int actionId, long currentTick, int windowTicks, bool consume)
        {
            if (actionId == UnsetActionId || windowTicks < 0) return false;

            for (int offset = _count - 1; offset >= 0; offset--)
            {
                ref Entry entry = ref _entries[Index(offset)];
                if (currentTick - entry.Tick > windowTicks) return false;
                if (entry.ActionId != actionId || entry.Consumed != 0) continue;
                if (consume) entry.Consumed = 1;
                return true;
            }

            return false;
        }

        private void PruneExpired(long tick)
        {
            while (_count > 0)
            {
                ref Entry entry = ref _entries[Index(0)];
                if (tick - entry.Tick <= _retentionTicks) return;
                entry.ActionId = UnsetActionId;
                entry.Consumed = 0;
                _head = _head + 1 == _capacity ? 0 : _head + 1;
                _count--;
            }
        }

        private int Index(int offset)
        {
            int index = _head + offset;
            return index >= _capacity ? index - _capacity : index;
        }
    }
}
