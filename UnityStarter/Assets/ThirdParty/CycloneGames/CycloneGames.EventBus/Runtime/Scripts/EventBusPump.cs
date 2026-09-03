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
    ///
    /// Structural changes during a drain are deferred: a flush callback that Adds, Removes or
    /// Clears while <see cref="Drain"/> is iterating takes effect when the drain completes, never
    /// mid-iteration. That keeps the target list immutable for the duration of the tick — a source
    /// that removes itself cannot skip its neighbour, a newly registered source cannot execute in
    /// the same tick that registered it, and a Clear cannot cut the tick short.
    /// </summary>
    public sealed class EventBusPump
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
        /// Registers a custom flush callback. Cold path: registration is not per event. During a
        /// drain the registration is deferred and takes effect when the drain completes.
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
        /// Removes a previously registered flush delegate. During a drain the removal is deferred
        /// and takes effect when the drain completes.
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
        /// Drains every source, publishing at most <paramref name="maxEventsPerTarget"/> from each.
        ///
        /// The budget is per source, not global, so one flooded queue cannot starve the others.
        /// Events a handler produces during the drain stay queued for the next tick, and structural
        /// changes (Add, Remove, Clear) from flush callbacks are applied when the drain completes.
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

            // Re-entrancy is rejected, not tolerated. A flush callback that drains again would
            // re-walk the same targets inside one tick, which breaks the per-tick budget ("a source
            // publishes at most N events this tick") and, via a self-registering callback, recurses
            // until the stack dies. Failing loudly is the only semantics that keeps the budget true.
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
                // Indexed loop, not foreach: List<T>'s struct enumerator would be fine here, but
                // the indexed form keeps the drain free of any enumerator state on every platform.
                // The list cannot mutate during the loop — mutations are deferred — so reading the
                // count every iteration is safe and simple.
                for (int index = 0; index < _targets.Count; index++)
                {
                    published += _targets[index](maxEventsPerTarget);
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
            return Drain(int.MaxValue);
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
