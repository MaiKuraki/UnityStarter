using System;
using System.Collections.Concurrent;

namespace CycloneGames.EventBus.Core
{
    /// <summary>
    /// Multi-producer, single-consumer (MPSC) bridge for crossing the thread boundary into a
    /// main-thread-confined <see cref="EventBus{T}"/>. Any number of background threads enqueue;
    /// exactly one owner thread (the Unity main thread) drains the queue into the bus.
    ///
    /// This is the correct way to reach the bus from background work (networking, asset loading, the
    /// Job System) without making the bus's zero-allocation lock-free hot path take a lock. The queue
    /// itself is thread-safe; the bus stays single-thread-confined because only the owner thread ever
    /// calls <see cref="FlushTo"/>.
    ///
    /// Enqueue allocates one internal node per event (the standard BCL concurrent-queue cost). The
    /// command/event hot path is <see cref="EventBus{T}.Publish"/>, which remains zero-allocation;
    /// this bridge is for the colder cross-thread ingress only.
    /// </summary>
    public sealed class ConcurrentEventQueue<T> where T : struct
    {
        private readonly ConcurrentQueue<T> _queue = new ConcurrentQueue<T>();

        public bool IsEmpty => _queue.IsEmpty;

        /// <summary>Thread-safe. Callable from any thread.</summary>
        public void Enqueue(in T evt)
        {
            _queue.Enqueue(evt);
        }

        /// <summary>
        /// Drains every pending event into the bus. Call only on the bus's owner thread.
        /// </summary>
        public void FlushTo(EventBus<T> bus)
        {
            if (bus == null)
            {
                throw new ArgumentNullException(nameof(bus));
            }

            while (_queue.TryDequeue(out T evt))
            {
                bus.Publish(in evt);
            }
        }

        /// <summary>
        /// Drains at most <paramref name="maxEvents"/> events so a single flush cannot stall the owner
        /// thread with an unbounded backlog. Returns the number of events published.
        /// </summary>
        public int FlushTo(EventBus<T> bus, int maxEvents)
        {
            if (bus == null)
            {
                throw new ArgumentNullException(nameof(bus));
            }

            if (maxEvents < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxEvents));
            }

            int flushed = 0;
            while (flushed < maxEvents && _queue.TryDequeue(out T evt))
            {
                bus.Publish(in evt);
                flushed++;
            }

            return flushed;
        }
    }
}
