using System;
using System.Collections.Generic;
using CycloneGames.EventBus.Core;

namespace CycloneGames.EventBus.Runtime
{
    /// <summary>
    /// Drains one buffered event into its bus. Returns the number of events published.
    ///
    /// Named delegate rather than a bare <c>Func&lt;int, int&gt;</c> so the registration signature is
    /// self-documenting at the callsite.
    /// </summary>
    /// <param name="maxEvents">Hard ceiling on how many events this call may publish.</param>
    public delegate int EventPumpFlush(int maxEvents);

    /// <summary>
    /// Frame-scheduling owner for buffered event sources: cross-thread
    /// <see cref="MpscEventQueue{T}"/> bridges and batched <see cref="EventStream{T}"/> writers.
    ///
    /// Why this exists: a queue with no owner has no drain point, and a drain with no budget turns a
    /// backlog into a frame spike. The pump gives both — one place that owns "when", and a per-source
    /// ceiling that owns "how much". Every source publishes the same bounded way on every platform,
    /// so the frame cost is a function of the configured budget rather than of whatever the network
    /// or the job system happened to deliver.
    ///
    /// The pump is plain C# and has no UnityEngine dependency: a headless server, a CLI tool or a
    /// test can drive it on their own tick. The Unity host is
    /// <see cref="EventBusPumpMonoBehaviour"/>, which is only a thin adapter.
    ///
    /// Single-thread-confined, like every bus it feeds.
    /// </summary>
    public sealed class EventBusPump
    {
        private readonly List<EventPumpFlush> _targets = new List<EventPumpFlush>();

        /// <summary>Registered sources.</summary>
        public int Count => _targets.Count;

        /// <summary>Registers a custom flush callback. Cold path: registration is not per event.</summary>
        public EventPumpFlush Add(EventPumpFlush flush)
        {
            if (flush == null)
            {
                throw new ArgumentNullException(nameof(flush));
            }

            _targets.Add(flush);
            return flush;
        }

        /// <summary>
        /// Registers a cross-thread queue. Returns the registered flush delegate; keep it to pass to
        /// <see cref="Remove"/> later.
        /// </summary>
        public EventPumpFlush AddQueue<T>(MpscEventQueue<T> queue, EventBus<T> bus) where T : struct
        {
            if (queue == null)
            {
                throw new ArgumentNullException(nameof(queue));
            }

            if (bus == null)
            {
                throw new ArgumentNullException(nameof(bus));
            }

            return Add(budget => queue.FlushTo(bus, budget));
        }

        /// <summary>
        /// Registers a batched stream. Returns the registered flush delegate; keep it to pass to
        /// <see cref="Remove"/> later.
        /// </summary>
        public EventPumpFlush AddStream<T>(EventStream<T> stream, EventBus<T> bus) where T : struct
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            if (bus == null)
            {
                throw new ArgumentNullException(nameof(bus));
            }

            return Add(budget => stream.FlushTo(bus, budget));
        }

        /// <summary>Removes a previously registered flush delegate.</summary>
        public bool Remove(EventPumpFlush flush)
        {
            return flush != null && _targets.Remove(flush);
        }

        /// <summary>
        /// Drains every source, publishing at most <paramref name="maxEventsPerTarget"/> from each.
        ///
        /// The budget is per source, not global, so one flooded queue cannot starve the others.
        /// Events a handler produces during the drain stay queued for the next tick.
        /// </summary>
        /// <returns>The total number of events published.</returns>
        public int Drain(int maxEventsPerTarget)
        {
            if (maxEventsPerTarget <= 0)
            {
                if (maxEventsPerTarget < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(maxEventsPerTarget));
                }

                return 0;
            }

            int published = 0;
            // Indexed loop, not foreach: List<T>'s struct enumerator would be fine here, but the
            // indexed form keeps the drain free of any enumerator state on every platform.
            for (int index = 0; index < _targets.Count; index++)
            {
                published += _targets[index](maxEventsPerTarget);
            }

            return published;
        }

        /// <summary>Drains every source to empty, subject to each source's own bounded semantics.</summary>
        /// <returns>The total number of events published.</returns>
        public int Drain()
        {
            return Drain(int.MaxValue);
        }

        /// <summary>
        /// Drops every registration. It does not discard the pending events themselves: clear the
        /// queues explicitly if that is what you want.
        /// </summary>
        public void Clear()
        {
            _targets.Clear();
        }
    }
}
