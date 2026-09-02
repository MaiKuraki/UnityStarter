using System;

namespace CycloneGames.EventBus.Core
{
    /// <summary>
    /// Bounded, preallocated linear buffer that batches events of one type and flushes them into an
    /// <see cref="EventBus{T}"/> at a moment the caller chooses.
    ///
    /// Why this exists: publishing thousands of events per frame one call at a time is correct but
    /// it makes the cost and the ordering implicit. A stream turns "N publishes scattered through the
    /// frame" into "write N, flush once", which gives three concrete properties:
    /// - Writes are sequential into one array, so they are cache-friendly and allocation-free.
    /// - The flush point is explicit, so dispatch timing is deterministic and reproducible instead
    ///   of interleaved with whatever else the frame was doing.
    /// - Flush can be budgeted (<see cref="FlushTo(EventBus{T}, int)"/>), which is what keeps a
    ///   pathological frame from stalling the main thread.
    ///
    /// Capacity is fixed at construction. <see cref="TryWrite"/> returns false when the stream is
    /// full and counts the drop; it never grows, so a runaway producer cannot allocate. Size the
    /// buffer to the worst-case frame, not the average one.
    ///
    /// Single-thread-confined, like the buses it flushes into.
    /// </summary>
    public sealed class EventStream<T> where T : struct
    {
        private readonly T[] _buffer;
        private int _count;
        private long _droppedCount;
        private long _rejectedCount;

        // Bumped by Clear. An in-flight FlushTo compares it against the value it captured on
        // entry: a mismatch means the batch it was publishing was cleared under it, so the loop
        // must stop and remove nothing. Appends do not bump it — they belong to the next round,
        // which the flush's entry-count bound already handles.
        private long _generation;

        // Guards re-entrant FlushTo from one of the stream's own handlers: an inner flush would
        // publish entries the outer round still owns and shift the buffer under its loop.
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
        /// Events that entered the stream and were then discarded unread by <see cref="Clear"/>.
        ///
        /// This counts confirmed loss only. A write the stream refused is not a loss: the caller may
        /// retry it. That case is <see cref="RejectedCount"/>.
        /// </summary>
        public long DroppedCount => _droppedCount;

        /// <summary>
        /// Write attempts refused because the stream was full. The signal that the capacity is too
        /// small for the batch, or that a flush was skipped.
        ///
        /// Not a loss count: a caller that retries on <c>false</c> loses nothing and still
        /// increments this.
        /// </summary>
        public long RejectedCount => _rejectedCount;

        /// <summary>True when no event is waiting.</summary>
        public bool IsEmpty => _count == 0;

        /// <summary>True when the stream is full and further writes will be refused.</summary>
        public bool IsFull => _count == _buffer.Length;

        /// <summary>
        /// Appends <paramref name="evt"/> and returns true, or returns false when the stream is full.
        /// Allocation-free in both cases.
        ///
        /// A false result counts toward <see cref="RejectedCount"/> and the event is the caller's to
        /// retry or discard. The stream cannot tell which one was meant.
        /// </summary>
        public bool TryWrite(in T evt)
        {
            if (_count == _buffer.Length)
            {
                _rejectedCount++;
                return false;
            }

            _buffer[_count++] = evt;
            return true;
        }

        /// <summary>
        /// Discards every pending event and adds the discarded total to <see cref="DroppedCount"/>.
        /// Allocation-free.
        ///
        /// These are the only events the stream can call lost: they entered it and were never read.
        ///
        /// Called from a flush handler it also invalidates the flush batch in progress: the flush
        /// stops immediately instead of publishing the zeroed buffer as default phantom events, and
        /// events written after the clear stay queued for the next round.
        /// </summary>
        public void Clear()
        {
            for (int index = 0; index < _count; index++)
            {
                _buffer[index] = default;
            }

            if (_count > 0)
            {
                _droppedCount += _count;
            }

            _count = 0;
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
        /// rest for a later flush. Use this to spread a large batch across frames.
        /// </summary>
        /// <returns>The number of events actually published, which can be lower than the budget
        /// when a handler clears the stream mid-flush.</returns>
        /// <exception cref="InvalidOperationException">
        /// A handler called <see cref="FlushTo(EventBus{T},int)"/> on this same stream while it was
        /// being flushed — the inner flush would publish entries the outer round still owns and
        /// shift the buffer under its loop.
        /// </exception>
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

            // Budget and generation are captured up front: handlers may append to the stream while
            // it is being flushed (the entry-count bound keeps those in the next round), and a
            // handler that calls Clear invalidates the whole batch — the entries this loop would
            // read next were just zeroed, so publishing them would fabricate default events.
            int entryCount = _count;
            int budget = entryCount < maxEvents ? entryCount : maxEvents;
            long generation = _generation;
            int published = 0;

            _flushing = true;
            try
            {
                for (int index = 0; index < budget; index++)
                {
                    bus.Publish(in _buffer[index]);
                    published++;

                    if (_generation != generation)
                    {
                        // The batch was cleared under us: everything left in it is gone, and
                        // events written after the clear belong to the next round. Remove nothing.
                        return published;
                    }
                }
            }
            finally
            {
                _flushing = false;
                if (_generation == generation)
                {
                    // Also runs when a handler faulted under PublishErrorPolicy.Stop: events
                    // already published leave the stream (no duplicate delivery on the next
                    // flush), the rest stay queued.
                    RemoveFront(published);
                }
            }

            return published;
        }

        private void RemoveFront(int count)
        {
            int remaining = _count - count;
            if (remaining < 0)
            {
                // Defensive: the generation guard keeps this branch unreachable today, but if a
                // future mutation slips past it, clamping here must not corrupt the buffer.
                remaining = 0;
            }

            if (remaining > 0)
            {
                Array.Copy(_buffer, count, _buffer, 0, remaining);
            }

            // Clearing the tail matters: event structs can hold references, and leaving them in the
            // buffer would pin those objects for the lifetime of the stream.
            for (int index = remaining; index < _count; index++)
            {
                _buffer[index] = default;
            }

            _count = remaining;
        }
    }
}
