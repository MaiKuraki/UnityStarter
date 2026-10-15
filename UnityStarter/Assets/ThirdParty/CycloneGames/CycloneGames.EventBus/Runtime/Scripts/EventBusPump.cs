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
    /// Frame-scheduling owner for buffered event sources (<see cref="MpscEventQueue{T}"/> bridges
    /// and <see cref="EventStream{T}"/> writers). A queue with no owner has no drain point, and a
    /// drain with no budget turns a backlog into a frame spike: the pump owns "when", a per-source
    /// ceiling, and a per-tick ceiling, so frame cost is a function of configuration.
    ///
    /// Plain C# with no UnityEngine dependency; <see cref="EventBusPumpMonoBehaviour"/> is the thin
    /// Unity host. Single-thread-confined. Structural changes (Add/Remove/Clear) during a drain are
    /// deferred to drain completion, keeping the target list immutable for the tick.
    /// </summary>
    public sealed class EventBusPump : IEventBusContainerDiagnostics
    {
        private readonly List<EventPumpFlush> _targets = new List<EventPumpFlush>();
        private readonly List<EventPumpFlush> _pendingAdds = new List<EventPumpFlush>();
        private readonly List<EventPumpFlush> _pendingRemovals = new List<EventPumpFlush>();
        private bool _clearPending;

        // Number of deferred adds that existed when Clear() was called during this drain. Adds
        // before the boundary belong to the registrations Clear removes; adds after it were made
        // *because of* the Clear and must survive it.
        private int _clearAddBoundary;
        private bool _clearBoundaryValid;
        private int _drainDepth;

        /// <summary>Registered sources.</summary>
        public int Count => _targets.Count;

        /// <summary>
        /// Diagnostics port. The pump schedules drains and holds no backlog of its own, so the only
        /// meaningful number is how many sources are registered: it is reported as Capacity. Pending
        /// / Dropped / Rejected / RejectedAfterClose are 0 and IsClosed is false. The queue and
        /// stream counters that actually describe backpressure are read from those containers
        /// directly, not from the pump.
        /// </summary>
        public EventBusContainerSnapshot GetContainerSnapshot()
        {
            return new EventBusContainerSnapshot(
                "EventBusPump",
                _targets.Count,
                0,
                0,
                0,
                0,
                false);
        }

        /// <summary>
        /// Registers a custom flush callback. During a drain the registration is deferred to
        /// drain completion.
        /// </summary>
        public EventPumpFlush Add(EventPumpFlush flush)
        {
            if (flush == null)
            {
                throw new ArgumentNullException(nameof(flush));
            }

            if (_drainDepth > 0)
            {
                // Deferred: a source registered by a running flush callback must not execute in
                // the same tick that registered it.
                _pendingAdds.Add(flush);
                return flush;
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

        /// <summary>
        /// Unregisters a flush delegate. During a drain the removal is deferred and takes effect when
        /// the drain completes.
        /// </summary>
        public bool Remove(EventPumpFlush flush)
        {
            if (flush == null)
            {
                return false;
            }

            if (_drainDepth > 0)
            {
                // An add-then-remove pair inside the same tick nets out to nothing.
                if (_pendingAdds.Remove(flush))
                {
                    return true;
                }

                if (_targets.Contains(flush))
                {
                    _pendingRemovals.Add(flush);
                    return true;
                }

                return false;
            }

            return _targets.Remove(flush);
        }

        /// <summary>
        /// Drains every source, publishing at most <paramref name="maxEventsPerTarget"/> from each and
        /// at most <paramref name="maxEventsPerTick"/> in total.
        /// </summary>
        public int Drain(int maxEventsPerTarget)
        {
            return Drain(maxEventsPerTarget, int.MaxValue);
        }

        /// <summary>
        /// Drains every source, publishing at most <paramref name="maxEventsPerTarget"/> from each
        /// (fairness: a flooded source cannot starve its neighbours) and at most
        /// <paramref name="maxEventsPerTick"/> in total (the frame bound). A tick value of 0 pauses
        /// publishing; negative is rejected. Events produced by handlers during the drain stay
        /// queued for the next tick.
        /// </summary>
        /// <returns>The total number of events published.</returns>
        public int Drain(int maxEventsPerTarget, int maxEventsPerTick)
        {
            if (maxEventsPerTarget <= 0)
            {
                if (maxEventsPerTarget < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(maxEventsPerTarget));
                }

                return 0;
            }

            if (maxEventsPerTick < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxEventsPerTick));
            }

            // Re-entrancy is rejected: an inner drain would break the per-tick budget guarantee.
            if (_drainDepth > 0)
            {
                throw new InvalidOperationException(
                    "EventBusPump.Drain is not re-entrant: a flush callback must not drain the pump "
                    + "it is being drained by. Split the work into a second pump, or defer it to the "
                    + "next tick.");
            }

            int published = 0;
            _drainDepth++;
            try
            {
                // Indexed loop: no enumerator state, and the list cannot mutate mid-drain
                // (mutations are deferred).
                for (int index = 0; index < _targets.Count; index++)
                {
                    if (published >= maxEventsPerTick)
                    {
                        // The tick is spent. Remaining sources keep their queued events for the next
                        // tick rather than being drained into an already expensive frame.
                        break;
                    }

                    int remaining = maxEventsPerTick - published;
                    int budget = maxEventsPerTarget < remaining ? maxEventsPerTarget : remaining;
                    published += _targets[index](budget);
                }
            }
            finally
            {
                if (--_drainDepth == 0)
                {
                    ApplyDeferredMutations();
                }
            }

            return published;
        }

        /// <summary>Drains every source to empty, subject to each source's own bounded semantics.</summary>
        /// <returns>The total number of events published.</returns>
        public int Drain()
        {
            return Drain(int.MaxValue, int.MaxValue);
        }

        /// <summary>
        /// Drops every registration. It does not discard the pending events themselves: clear the
        /// queues explicitly if that is what you want. During a drain the clear is deferred and
        /// takes effect when the drain completes.
        /// </summary>
        public void Clear()
        {
            if (_drainDepth > 0)
            {
                // Deferred, and ordering matters: a callback that calls Clear() and then Add() must
                // end up with the new source registered. Record how many deferred adds existed when
                // Clear was requested; ApplyDeferredMutations drops those and keeps the rest.
                if (!_clearBoundaryValid)
                {
                    _clearAddBoundary = _pendingAdds.Count;
                    _clearBoundaryValid = true;
                }

                _clearPending = true;
                return;
            }

            _targets.Clear();
        }

        private void ApplyDeferredMutations()
        {
            int addStart = 0;

            if (_clearPending)
            {
                _clearPending = false;

                // Everything registered before the Clear request is removed by it; everything
                // added after it survives and starts running on the next tick.
                if (_clearBoundaryValid)
                {
                    addStart = _clearAddBoundary;
                    _clearBoundaryValid = false;
                    _clearAddBoundary = 0;
                }
                else
                {
                    addStart = _pendingAdds.Count;
                }

                _pendingRemovals.Clear();
                _targets.Clear();
            }

            // Adds first, then removals: an add and a remove of the same delegate in one tick is
            // already netted out above, so the two lists never fight here.
            for (int index = addStart; index < _pendingAdds.Count; index++)
            {
                _targets.Add(_pendingAdds[index]);
            }

            _pendingAdds.Clear();

            for (int index = 0; index < _pendingRemovals.Count; index++)
            {
                _targets.Remove(_pendingRemovals[index]);
            }

            _pendingRemovals.Clear();
        }
    }
}
