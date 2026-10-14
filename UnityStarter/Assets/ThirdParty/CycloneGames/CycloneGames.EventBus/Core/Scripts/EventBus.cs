using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace CycloneGames.EventBus.Core
{
    /// <summary>
    /// Zero-allocation one-to-many synchronous notification bus. Handlers run in subscription
    /// order; <see cref="Publish"/> performs no managed allocation, no boxing, no closure and no
    /// logging on the dispatch path.
    ///
    /// Thread model: single-thread-confined. All members except <see cref="ScheduleRemoval"/> must
    /// run on one owner thread; confinement is the safety guarantee and the precondition for being
    /// lock-free. Cross a thread boundary through <see cref="MpscEventQueue{T}"/> drained on the
    /// owner thread; a foreign-thread callback releases subscriptions via <see cref="ScheduleRemoval"/>.
    ///
    /// Structural changes during dispatch: a handler subscribed mid-round fires from the next
    /// round; a handler unsubscribed mid-round is skipped from that point; compaction and teardown
    /// are deferred to the outermost dispatch frame, so slot indices never shift under an active
    /// iteration and a dispatch round is atomic.
    /// </summary>
    public sealed class EventBus<T> : IDisposable, IEventBusDiagnostics, IEventBusSubscriberInspection where T : struct
    {
        /// <summary>Storage reserved when no explicit capacity is supplied.</summary>
        private const int DefaultCapacity = 8;

        // Compact once tombstones reach 1/N of the occupied slots. Compaction is O(_slots); a
        // proportional (not absolute) threshold keeps an unsubscribe amortized O(1).
        private const int CompactRatio = 3;

        // Upper bound on retained handles so a churn-heavy bus cannot pin them without limit.
        private const int HandlePoolLimit = 32;

        // Deferred-unsubscribe inbox close states (documented on _closeState).
        private const int Open = 0;
        private const int DisposeRequested = 1;
        private const int Closed = 2;

        private readonly int _maxDispatchDepth;
        private readonly string _category;
        private readonly IEventBusLogSink _logSink;
        private readonly PublishErrorPolicy _publishErrorPolicy;

        // Handle stored beside the handler so a handle releases its slot in O(1); compaction moves
        // both fields together and re-stamps the handle, so the mapping never goes stale.
        private struct HandlerSlot
        {
            public Action<T> Handler;
            public EventSubscription<T> Handle;
        }

        private HandlerSlot[] _handlers;
        private int _slots;
        private int _tombstones;
        private int _dispatchDepth;
        private bool _compactPending;

        // Owner-thread fast flag for the entry-point checks. The teardown trigger is _closeState,
        // the properly synchronized field.
        private bool _disposed;

        // Deferred-unsubscribe inbox for foreign threads; the owner thread applies removals at its
        // next entry point. The count keeps the hot publish path at one volatile read.
        private readonly ConcurrentQueue<IEventSubscription> _pendingRemovals =
            new ConcurrentQueue<IEventSubscription>();
        private int _pendingRemovalCount;

        private EventSubscription<T>[] _handlePool;
        private int _handlePoolCount;

        // Close protocol for the inbox (see ScheduleRemoval):
        //   Open: foreign threads enqueue removals for the owner thread.
        //   DisposeRequested: teardown pending or running; enqueue still accepted, teardown drains.
        //   Closed: teardown done; foreign threads must release synchronously.
        // Re-checked after every enqueue so a check-then-enqueue race cannot strand a subscription.
        private int _closeState;

        // Serializes "enqueue + re-check" against "publish Closed + final drain". Touched only by
        // the foreign-thread cold path (ScheduleRemoval) and the one-shot teardown.
        private readonly object _closeGate = new object();

        // Exactly-once claim on TeardownCore between the outermost dispatch frame and a
        // foreign-thread Dispose. 0 = unclaimed, 1 = claimed.
        private int _teardownClaimed;

        private long _publishCount;
        private long _droppedReentrantCount;
        private long _subscriberErrorCount;
        private int _peakSubscriptionCount;

        public EventBus(EventBusConfiguration configuration = null)
            : this(configuration, DefaultCapacity)
        {
        }

        /// <param name="configuration">Immutable composition choices; <c>null</c> selects defaults.</param>
        /// <param name="initialCapacity">
        /// Slots reserved up front; size to the steady-state subscriber count to avoid array growth.
        /// </param>
        public EventBus(EventBusConfiguration configuration, int initialCapacity)
        {
            if (initialCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));
            }

            configuration ??= EventBusConfiguration.Default;

            _maxDispatchDepth = configuration.MaxDispatchDepth;
            _logSink = configuration.LogSink ?? NullEventBusLogSink.Instance;
            _publishErrorPolicy = configuration.PublishErrorPolicy;
            _category = typeof(T).Name;
            _handlers = new HandlerSlot[initialCapacity];
        }

        /// <summary>Live subscribers. O(1).</summary>
        public int SubscriptionCount => _slots - _tombstones;

        /// <summary>
        /// Dead slots left by unsubscribe. Bounded by the compaction ratio; a value that keeps
        /// climbing signals subscription churn that should be gated with an activity flag.
        /// </summary>
        public int TombstoneCount => _tombstones;

        // Long counters are written by the owner thread only; Interlocked.Read keeps reads from
        // tooling threads (the debugger window) free of torn values on 32-bit platforms.
        /// <summary>Total successful dispatch entries since construction.</summary>
        public long PublishCount => Interlocked.Read(ref _publishCount);

        /// <summary>Publishes rejected because <see cref="DispatchDepth"/> hit the configured limit.</summary>
        public long DroppedReentrantCount => Interlocked.Read(ref _droppedReentrantCount);

        /// <summary>Subscriber exceptions handled according to the configured policy.</summary>
        public long SubscriberErrorCount => Interlocked.Read(ref _subscriberErrorCount);

        /// <summary>Highest live subscriber count observed. Use it to size <c>initialCapacity</c>.</summary>
        public int PeakSubscriptionCount => _peakSubscriptionCount;

        /// <summary>Current dispatch nesting depth; zero when idle.</summary>
        public int DispatchDepth => _dispatchDepth;

        /// <summary>Backing array length. Grows only on subscribe, never shrinks.</summary>
        public int Capacity => _handlers.Length;

        public bool IsDisposed => _disposed;

        public string EventTypeName => typeof(T).FullName;

        /// <summary>
        /// Registers <paramref name="handler"/> and returns a pooled handle that unsubscribes on
        /// disposal. Cache the delegate at the callsite: <c>bus.Subscribe(OnDamage)</c> allocates a
        /// new delegate every call because C# does not cache instance method groups.
        /// </summary>
        public IEventSubscription Subscribe(Action<T> handler)
        {
            ThrowIfDisposed();
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            // Tombstone reuse is only safe outside dispatch: reusing a slot below the dispatch
            // loop's count snapshot would let a mid-round subscribe fire in that same round.
            DrainPendingRemovals();
            if (_dispatchDepth == 0 && _tombstones > 0)
            {
                for (int index = 0; index < _slots; index++)
                {
                    if (_handlers[index].Handler != null)
                    {
                        continue;
                    }

                    _tombstones--;
                    TrackPeak();
                    LogSubscription("subscribed (reused tombstone slot)");
                    return PlaceHandler(handler, index);
                }
            }

            EnsureCapacity(_slots + 1);
            int slot = _slots++;
            TrackPeak();
            LogSubscription("subscribed");
            return PlaceHandler(handler, slot);
        }

        // The handle is rented before the slot is written, because the slot records it.
        private IEventSubscription PlaceHandler(Action<T> handler, int index)
        {
            EventSubscription<T> handle = RentHandle(handler, index);
            _handlers[index].Handler = handler;
            _handlers[index].Handle = handle;
            return handle;
        }

        /// <summary>
        /// Removes <paramref name="handler"/>; returns true when it was subscribed. A no-op on a
        /// disposed bus, which keeps deferred scope disposal (OnDestroy after context teardown) safe.
        /// </summary>
        public bool Unsubscribe(Action<T> handler)
        {
            if (_disposed)
            {
                return false;
            }

            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            return RemoveCore(handler);
        }

        /// <summary>
        /// Dispatches <paramref name="evt"/> to every subscriber in subscription order.
        /// Zero allocation on the happy path. Subscriber exceptions follow the configured
        /// <see cref="PublishErrorPolicy"/>; see <see cref="EventBusConfiguration"/>.
        /// </summary>
        public void Publish(in T evt)
        {
            if (_disposed)
            {
                ThrowDisposed();
            }

            // Apply deferred foreign-thread removals before the round snapshots its state.
            DrainPendingRemovals();

            // Re-entrancy ceiling: an unbounded recursive publish chain would otherwise end in an
            // unrecoverable stack overflow. The drop is counted and logged.
            if (_dispatchDepth >= _maxDispatchDepth)
            {
                _droppedReentrantCount++;
                LogDroppedReentrant();
                return;
            }

            _dispatchDepth++;
            _publishCount++;

            // Per-frame, not per-bus: a nested dispatch must not inherit the outer frame's fault.
            ExceptionDispatchInfo pendingFault = null;
            try
            {
                // The array field is re-read every iteration: a mid-dispatch subscribe can replace
                // the backing array, and a hoisted local would keep invoking forgotten handlers.
                int count = _slots;
                for (int index = 0; index < count; index++)
                {
                    Action<T> handler = _handlers[index].Handler;
                    if (handler == null)
                    {
                        continue;
                    }

                    try
                    {
                        handler(evt);
                    }
                    catch (Exception exception)
                    {
                        // Every fault is routed for counting/logging; only the first is rethrown.
                        ExceptionDispatchInfo fault = OnSubscriberException(exception);
                        pendingFault ??= fault;
                    }
                }

                if (pendingFault != null)
                {
                    // Original stack preserved; the finally still restores depth first.
                    pendingFault.Throw();
                }
            }
            finally
            {
                // Depth restored unconditionally; teardown and compaction deferred to the outermost
                // frame so slot indices never shift under an in-flight iteration.
                if (--_dispatchDepth == 0)
                {
                    // _closeState (not _disposed) is the synchronized field a foreign-thread Dispose
                    // exchanges, so a cross-thread disposal cannot be missed here.
                    if (Volatile.Read(ref _closeState) != Open)
                    {
                        TeardownCore();
                    }
                    else if (_compactPending)
                    {
                        _compactPending = false;
                        CompactInPlace();
                    }
                }
            }
        }

        /// <summary>
        /// Removes tombstone slots in place. Allocation-free and non-shrinking. Runs automatically
        /// on the unsubscribe path; direct calls are for a one-shot reclaim after a burst.
        /// </summary>
        /// <exception cref="InvalidOperationException">Dispatch is in progress.</exception>
        public void Compact()
        {
            ThrowIfDisposed();
            if (_dispatchDepth != 0)
            {
                throw new InvalidOperationException(
                    "Compact cannot run during dispatch; slot indices would shift under the active iteration.");
            }

            CompactInPlace();
            Log("compacted");
        }

        /// <summary>
        /// Removes every subscription without disposing the bus, so the instance can be reused
        /// across a reset. Diagnostic counters are preserved: they describe the bus lifetime.
        /// </summary>
        /// <exception cref="InvalidOperationException">Dispatch is in progress.</exception>
        public void Clear()
        {
            ThrowIfDisposed();
            if (_dispatchDepth != 0)
            {
                throw new InvalidOperationException("Clear cannot run during dispatch.");
            }

            DrainPendingRemovals();
            ClearCore();
            Log("cleared");
        }

        public EventBusSnapshot GetSnapshot()
        {
            return new EventBusSnapshot(
                SubscriptionCount,
                TombstoneCount,
                Interlocked.Read(ref _publishCount),
                Interlocked.Read(ref _droppedReentrantCount),
                Interlocked.Read(ref _subscriberErrorCount),
                _peakSubscriptionCount,
                _dispatchDepth,
                _handlers.Length,
                _disposed);
        }

        /// <inheritdoc cref="IEventBusSubscriberInspection.CopySubscribersTo"/>
        public void CopySubscribersTo(List<EventSubscriberInfo> destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            for (int index = 0; index < _slots; index++)
            {
                Action<T> handler = _handlers[index].Handler;
                if (handler == null)
                {
                    continue;
                }

                destination.Add(new EventSubscriberInfo
                {
                    MethodName = handler.Method.Name,
                    DeclaringTypeName = handler.Method.DeclaringType?.Name ?? string.Empty,
                    Target = handler.Target,
                });
            }
        }

        /// <summary>
        /// Tears the bus down. Idempotent. During an active dispatch round the teardown is
        /// deferred: the round is atomic, <see cref="IsDisposed"/> turns true immediately (nested
        /// Publish/Subscribe throw), and the outermost dispatch frame tears down on exit.
        /// Owner thread only, like every other member.
        /// </summary>
        public void Dispose()
        {
            // Atomic close handshake: the first caller wins; later calls are no-ops.
            if (Interlocked.CompareExchange(ref _closeState, DisposeRequested, Open) != Open)
            {
                return;
            }

            _disposed = true;

            if (_dispatchDepth > 0)
            {
                // An in-flight Publish owns the teardown; the outermost frame re-enters
                // TeardownCore on exit.
                return;
            }

            TeardownCore();
        }

        /// <summary>
        /// Thread-safe deferred unsubscribe — the only sanctioned way to release a subscription
        /// from a non-owner thread. The removal is applied on the owner thread at the next
        /// <see cref="Publish"/>, <see cref="Subscribe"/>, <see cref="Clear"/> or Dispose.
        /// </summary>
        public void ScheduleRemoval(IEventSubscription subscription)
        {
            if (subscription == null)
            {
                throw new ArgumentNullException(nameof(subscription));
            }

            lock (_closeGate)
            {
                // Closed: nothing drains the inbox again; release synchronously. Safe because
                // Release short-circuits on a disposed bus and Dispose is idempotent.
                if (_closeState == Closed)
                {
                    subscription.Dispose();
                    return;
                }

                _pendingRemovals.Enqueue(subscription);
                Interlocked.Increment(ref _pendingRemovalCount);

                // Re-check after enqueueing: if the bus closed in between, the teardown drain may
                // already have run past this entry, so release it here.
                if (_closeState == Closed)
                {
                    DrainPendingRemovals();
                }
            }
        }

        // Non-zero after Dispose indicates a close-protocol leak; tests assert on it.
        internal int PendingRemovalCount => Volatile.Read(ref _pendingRemovalCount);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DrainPendingRemovals()
        {
            // One volatile read on the steady-state path; the queue is only touched when a foreign
            // thread actually scheduled something.
            if (Volatile.Read(ref _pendingRemovalCount) == 0)
            {
                return;
            }

            int drained = 0;
            while (_pendingRemovals.TryDequeue(out IEventSubscription subscription))
            {
                drained++;

                // A subscription may be disposed directly as well (owner-thread double release);
                // Dispose is idempotent, so a duplicate here is harmless.
                subscription.Dispose();
            }

            if (drained > 0)
            {
                // One atomic update for the whole batch instead of one per entry.
                Interlocked.Add(ref _pendingRemovalCount, -drained);
            }
        }

        private void TeardownCore()
        {
            // Exactly-once claim: the loser must not clear the handler array under the winner.
            if (Interlocked.Exchange(ref _teardownClaimed, 1) != 0)
            {
                return;
            }

            // A dispatch may still be iterating; clearing now would violate the atomic-round
            // guarantee. Release the claim so the outermost dispatch frame performs the teardown.
            if (Volatile.Read(ref _dispatchDepth) > 0)
            {
                Interlocked.Exchange(ref _teardownClaimed, 0);
                return;
            }

            // Release the backing array and pool so a retained bus no longer pins them.
            DrainPendingRemovals();
            ClearCore();
            _handlers = Array.Empty<HandlerSlot>();
            _handlePool = null;
            _handlePoolCount = 0;

            // Publish Closed and run the final drain in one critical section against
            // ScheduleRemoval: either the enqueuer sees Open and this drain collects its entry,
            // or it sees Closed and releases the entry itself.
            lock (_closeGate)
            {
                Volatile.Write(ref _closeState, Closed);
                DrainPendingRemovals();
            }

            Log("disposed");
        }

        internal void Release(EventSubscription<T> handle, Action<T> handler, int slot)
        {
            if (_disposed)
            {
                return;
            }

            // Negative slot: already reclaimed without this handle (identity Unsubscribe, Clear, or
            // teardown). Nothing to remove; an identity scan could hit an unrelated subscription
            // sharing the delegate.
            if (slot >= 0)
            {
                // O(1) fast path: the slot records its own handle and compaction re-stamps it, so
                // identity proves ownership. The scan is a defensive fallback only.
                if (slot < _slots && ReferenceEquals(_handlers[slot].Handle, handle))
                {
                    MarkSlotDead(slot);
                }
                else
                {
                    RemoveCore(handler);
                }
            }

            if (_handlePool == null)
            {
                _handlePool = new EventSubscription<T>[4];
            }
            else if (_handlePoolCount == _handlePool.Length)
            {
                if (_handlePoolCount >= HandlePoolLimit)
                {
                    // Pool is full: let this handle be collected instead of growing without bound.
                    return;
                }

                var grown = new EventSubscription<T>[_handlePoolCount * 2];
                Array.Copy(_handlePool, grown, _handlePoolCount);
                _handlePool = grown;
            }

            _handlePool[_handlePoolCount++] = handle;
        }

        private bool RemoveCore(Action<T> handler)
        {
            // Hoisting is safe: nothing in this method can re-enter the bus.
            HandlerSlot[] handlers = _handlers;
            for (int index = 0; index < _slots; index++)
            {
                if (handlers[index].Handler != handler)
                {
                    continue;
                }

                MarkSlotDead(index);
                return true;
            }

            return false;
        }

        // Kills one live slot and runs the compaction threshold check. Shared by identity-based
        // Unsubscribe and the O(1) handle release so both maintain the same invariants.
        private void MarkSlotDead(int index)
        {
            _handlers[index].Handle.DetachSlot();
            _handlers[index].Handler = null;
            _handlers[index].Handle = null;
            _tombstones++;

            // Maintenance lives on the unsubscribe path (the only tombstone producer), never on
            // the dispatch path.
            if (_dispatchDepth == 0)
            {
                MaybeCompact();
            }
            else
            {
                _compactPending = true;
            }

            LogSubscription("unsubscribed");
        }

        // Proportional threshold only: an absolute count would make an unsubscribe's amortized
        // cost grow with the subscriber count, since compaction is O(_slots).
        private void MaybeCompact()
        {
            if (_tombstones == 0)
            {
                return;
            }

            if (_tombstones * CompactRatio >= _slots)
            {
                CompactInPlace();
            }
        }

        private void CompactInPlace()
        {
            HandlerSlot[] handlers = _handlers;
            int writeIndex = 0;
            for (int readIndex = 0; readIndex < _slots; readIndex++)
            {
                Action<T> handler = handlers[readIndex].Handler;
                if (handler == null)
                {
                    continue;
                }

                // The handle moves with its handler and is re-stamped, keeping the O(1) release
                // path valid. Compaction is the only relocator of live slots.
                EventSubscription<T> handle = handlers[readIndex].Handle;
                handle.MoveToSlot(writeIndex);
                handlers[writeIndex].Handler = handler;
                handlers[writeIndex].Handle = handle;
                writeIndex++;
            }

            // The tail must be cleared: stale delegates keep subscriber objects alive.
            for (int index = writeIndex; index < _slots; index++)
            {
                handlers[index].Handler = null;
                handlers[index].Handle = null;
            }

            _slots = writeIndex;
            _tombstones = 0;
        }

        private void ClearCore()
        {
            HandlerSlot[] handlers = _handlers;
            for (int index = 0; index < _slots; index++)
            {
                // Detach first: an undisposed handle must not address a reassigned slot later.
                handlers[index].Handle?.DetachSlot();
                handlers[index].Handler = null;
                handlers[index].Handle = null;
            }

            _slots = 0;
            _tombstones = 0;
            _compactPending = false;
        }

        private EventSubscription<T> RentHandle(Action<T> handler, int slot)
        {
            if (_handlePoolCount > 0)
            {
                EventSubscription<T> pooled = _handlePool[--_handlePoolCount];
                _handlePool[_handlePoolCount] = null;
                pooled.Reset(this, handler, slot);
                return pooled;
            }

            return new EventSubscription<T>(this, handler, slot);
        }

        private void TrackPeak()
        {
            int live = _slots - _tombstones;
            if (live > _peakSubscriptionCount)
            {
                _peakSubscriptionCount = live;
            }
        }

        // Cold-path exception routing, out of line to keep the dispatch loop tight. Returns the
        // fault to rethrow after the round, or null when the policy swallows it. Under Stop it
        // rethrows here, aborting the round.
        [MethodImpl(MethodImplOptions.NoInlining)]
        private ExceptionDispatchInfo OnSubscriberException(Exception exception)
        {
            _subscriberErrorCount++;
            LogException(exception);

            // Capture preserves the original stack; `throw exception` would rewrite it.
            ExceptionDispatchInfo captured = ExceptionDispatchInfo.Capture(exception);

            if (_publishErrorPolicy == PublishErrorPolicy.Stop)
            {
                captured.Throw();
            }

            return _publishErrorPolicy == PublishErrorPolicy.ContinueOnError ? captured : null;
        }

        // Cold path; the enable check precedes any work and the message is a constant.
        private void LogDroppedReentrant()
        {
            if (!_logSink.IsEnabled(EventBusLogSeverity.Warning, _category))
            {
                return;
            }

            _logSink.Write(
                EventBusLogSeverity.Warning,
                _category,
                "Publish dropped: the dispatch depth ceiling was reached. A handler is publishing "
                + "its own event type, directly or through a cycle. The event was not delivered to "
                + "any subscriber.");
        }

        private void EnsureCapacity(int required)
        {
            if (required <= _handlers.Length)
            {
                return;
            }

            int nextCapacity = Math.Max(required, _handlers.Length * 2);
            var next = new HandlerSlot[nextCapacity];
            Array.Copy(_handlers, next, _slots);
            _handlers = next;
        }

        private void LogSubscription(string operation)
        {
            // No reflection: the delegate method name is omitted to stay AOT-safe.
            Log(operation);
        }

        private void LogException(Exception exception)
        {
            if (!_logSink.IsEnabled(EventBusLogSeverity.Error, _category))
            {
                return;
            }

            _logSink.WriteException(
                EventBusLogSeverity.Error,
                _category,
                exception,
                "A subscriber threw while publishing.");
        }

        private void Log(string message)
        {
            // The enable check stays ahead of any work; every message here is a constant literal.
            if (!_logSink.IsEnabled(EventBusLogSeverity.Debug, _category))
            {
                return;
            }

            _logSink.Write(EventBusLogSeverity.Debug, _category, message);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                ThrowDisposed();
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void ThrowDisposed()
        {
            throw new ObjectDisposedException(typeof(EventBus<T>).FullName);
        }
    }
}
