using System;

namespace CycloneGames.EventBus.Core
{
    /// <summary>
    /// Bounded, preallocated ring buffer that batches events of one type and flushes them into an
    /// <see cref="EventBus{T}"/> at a caller-chosen point. Turns "N publishes scattered through a
    /// frame" into "write N, flush once": sequential writes, an explicit dispatch point, and a
    /// budgetable flush (<see cref="FlushTo(EventBus{T}, int)"/>).
    ///
    /// Capacity is fixed at construction; <see cref="TryWrite"/> returns false when full and never
    /// grows, so a runaway producer cannot allocate. Size to the worst-case frame.
    /// Single-thread-confined, like the buses it flushes into.
    /// </summary>
    public sealed class EventStream<T> : IEventBusContainerDiagnostics where T : struct
    {
        private readonly T[] _buffer;

        // Logical index of the oldest pending event; tail = head + count. A ring rather than a
        // shift-down queue so a budgeted flush costs no element moves.
        private int _head;

        private int _count;
        private long _droppedCount;
        private long _rejectedCount;

        // Bumped by Clear; an in-flight FlushTo compares it against the value captured on entry
        // and stops on mismatch instead of publishing zeroed entries.
        private long _generation;

        // Rejects re-entrant FlushTo from one of the stream's own handlers.
        private bool _flushing;

        /// <param name="capacity">Fixed event capacity; must be at least 1.</param>
        public EventStream(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _buffer = new T[capacity];
        }

        /// <summary>Fixed event capacity.</summary>
        public int Capacity => _buffer.Length;

        /// <summary>Events waiting to be flushed.</summary>
        public int Count => _count;

        /// <summary>
        /// Events that entered the stream and were discarded unread by <see cref="Clear"/>.
        /// Confirmed loss only; a refused write is <see cref="RejectedCount"/>.
        /// </summary>
        public long DroppedCount => _droppedCount;

        /// <summary>
        /// Write attempts refused because the stream was full. Signals undersized capacity or a
        /// skipped flush. Not a loss count: a caller that retries loses nothing.
        /// </summary>
        public long RejectedCount => _rejectedCount;

        /// <inheritdoc cref="IEventBusContainerDiagnostics.GetContainerSnapshot"/>
        public EventBusContainerSnapshot GetContainerSnapshot()
        {
            // The stream has no close protocol, so RejectedAfterCloseCount stays 0 and IsClosed is
            // always false.
            return new EventBusContainerSnapshot(
                "EventStream<" + typeof(T).Name + ">",
                Capacity,
                Count,
                DroppedCount,
                RejectedCount,
                0,
                false);
        }

        /// <summary>True when no event is waiting.</summary>
        public bool IsEmpty => _count == 0;

        /// <summary>True when the stream is full and further writes will be refused.</summary>
        public bool IsFull => _count == _buffer.Length;

        /// <summary>
        /// Appends <paramref name="evt"/> and returns true, or returns false when full.
        /// Allocation-free. A false result counts toward <see cref="RejectedCount"/>; the event is
        /// the caller's to retry or discard.
        /// </summary>
        public bool TryWrite(in T evt)
        {
            if (_count == _buffer.Length)
            {
                _rejectedCount++;
                return false;
            }

            int tail = _head + _count;
            if (tail >= _buffer.Length)
            {
                // head and count are both below Length, so one conditional subtract always wraps.
                tail -= _buffer.Length;
            }

            _buffer[tail] = evt;
            _count++;
            return true;
        }

        /// <summary>
        /// Discards every pending event and adds the total to <see cref="DroppedCount"/>.
        /// Allocation-free. Called from a flush handler it invalidates the batch in progress:
        /// the flush stops instead of publishing zeroed entries, and events written after the
        /// clear stay queued for the next round.
        /// </summary>
        public void Clear()
        {
            for (int index = 0; index < _count; index++)
            {
                _buffer[PhysicalIndex(index)] = default;
            }

            if (_count > 0)
            {
                _droppedCount += _count;
            }

            _count = 0;
            _head = 0;
            _generation++;
        }

        /// <summary>Publishes every pending event in write order and empties the stream.</summary>
        /// <returns>The number of events published.</returns>
        public int FlushTo(EventBus<T> bus)
        {
            return FlushTo(bus, int.MaxValue);
        }

        /// <summary>
        /// Publishes at most <paramref name="maxEvents"/> pending events in write order, leaving the
        /// rest for a later flush.
        /// </summary>
        /// <returns>The number of events actually published; lower than the budget when a handler
        /// clears the stream mid-flush.</returns>
        /// <exception cref="InvalidOperationException">Re-entered from one of its own handlers.</exception>
        public int FlushTo(EventBus<T> bus, int maxEvents)
        {
            if (bus == null)
            {
                throw new ArgumentNullException(nameof(bus));
            }

            if (maxEvents <= 0)
            {
                if (maxEvents < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(maxEvents));
                }

                return 0;
            }

            if (_flushing)
            {
                throw new InvalidOperationException(
                    "FlushTo cannot be re-entered on the same stream from one of its own handlers.");
            }

            // Budget and generation captured up front: handler appends stay for the next round
            // (bounded by the entry count), and a handler Clear invalidates the batch.
            int entryCount = _count;
            int budget = entryCount < maxEvents ? entryCount : maxEvents;
            long generation = _generation;
            int published = 0;

            _flushing = true;
            try
            {
                for (int index = 0; index < budget; index++)
                {
                    bus.Publish(in _buffer[PhysicalIndex(index)]);
                    published++;

                    if (_generation != generation)
                    {
                        // Cleared mid-flush: the rest of the batch is gone, later writes belong to
                        // the next round. Remove nothing.
                        return published;
                    }
                }
            }
            finally
            {
                _flushing = false;
                if (_generation == generation)
                {
                    // Also runs when a handler faulted under Stop: published events leave the
                    // stream (no duplicate delivery), the rest stay queued.
                    RemoveFront(published);
                }
            }

            return published;
        }

        // Drops the oldest entries by advancing the head, with no element moves.
        private void RemoveFront(int count)
        {
            int removed = count;
            if (removed > _count)
            {
                removed = _count;
            }

            // Clearing matters: event structs can hold references that would otherwise be pinned
            // for the stream's lifetime.
            for (int index = 0; index < removed; index++)
            {
                _buffer[PhysicalIndex(index)] = default;
            }

            _head += removed;
            if (_head >= _buffer.Length)
            {
                _head -= _buffer.Length;
            }

            _count -= removed;

            if (_count == 0)
            {
                // A drained stream restarts writes at index 0.
                _head = 0;
            }
        }

        // Maps a logical index (0 = oldest pending) to its ring slot.
        private int PhysicalIndex(int logicalIndex)
        {
            int index = _head + logicalIndex;
            return index >= _buffer.Length ? index - _buffer.Length : index;
        }
    }
}
