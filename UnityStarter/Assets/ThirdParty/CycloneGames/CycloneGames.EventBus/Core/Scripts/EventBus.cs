using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace CycloneGames.EventBus.Core
{
    /// <summary>
    /// Zero-allocation one-to-many notification bus. This is the hot path: <see cref="Publish"/> is
    /// synchronous, deterministic, and performs no managed allocation, no boxing, no LINQ, no
    /// closure, no string formatting and no logging on the dispatch path.
    ///
    /// Thread model: single-thread-confined. Subscribe, Unsubscribe, Publish, Compact, Clear and
    /// Dispose must all run on one owner thread (in Unity, the main thread). Confinement is the
    /// safety guarantee and the precondition for zero allocation, so <see cref="Publish"/> takes no
    /// lock. To cross a thread boundary, publish into an <see cref="MpscEventQueue{T}"/> from the
    /// background and drain it into this bus on the owner thread. A callback that fires on a
    /// foreign thread (a cancellation registration, a foreign completion) releases its
    /// subscription through <see cref="ScheduleRemoval"/> instead of touching the bus directly.
    ///
    /// Ordering: handlers run in subscription order, on every platform and every scripting backend.
    /// There is no priority, no reordering and no async completion, so a publish is a fully
    /// deterministic call tree that can be reasoned about and profiled frame by frame.
    ///
    /// Structural changes during dispatch:
    /// - A handler subscribed during Publish never fires in that round: it lands at or beyond the
    ///   slot-count snapshot the loop captured on entry.
    /// - A handler unsubscribed during Publish is skipped from the moment its slot reads null, so a
    ///   handler that removes a later handler suppresses it within the same round.
    /// - Compaction never runs mid-dispatch. An unsubscribe during dispatch marks the bus and the
    ///   outermost dispatch frame performs a single in-place compaction on exit.
    /// - Dispose during Publish is also deferred: the round is atomic, so the handlers captured by
    ///   the loop still run, <see cref="IsDisposed"/> turns true immediately (nested Publish and
    ///   Subscribe throw), and the outermost dispatch frame tears the bus down on exit.
    /// </summary>
    public sealed class EventBus<T> : IDisposable, IEventBusDiagnostics where T : struct
    {
        /// <summary>Storage reserved when no explicit capacity is supplied.</summary>
        private const int DefaultCapacity = 8;

        /// <summary>
        /// Compact once tombstones reach 1/N of the occupied slots. The ratio is required for
        /// amortization: compaction is O(_slots), so an absolute tombstone count would make the cost
        /// of an unsubscribe grow with the subscriber count.
        /// </summary>
        private const int CompactRatio = 3;

        /// <summary>
        /// Upper bound on retained subscription handles. Bounding the pool keeps a churn-heavy bus
        /// from pinning an unbounded number of handles; surplus handles are simply collected.
        /// </summary>
        private const int HandlePoolLimit = 32;

        // Deferred-unsubscribe inbox close states (documented on _closeState).
        private const int Open = 0;
        private const int DisposeRequested = 1;
        private const int Closed = 2;

        private readonly int _maxDispatchDepth;
        private readonly string _category;
        private readonly IEventBusLogSink _logSink;
        private readonly PublishErrorPolicy _publishErrorPolicy;

        /// <summary>
        /// One subscription slot. The owning handle is stored beside the handler rather than looked
        /// up from it, which is what lets a handle release its slot in O(1): compaction moves both
        /// fields together and re-stamps the handle, so the mapping can never go stale. Keeping them
        /// in one array rather than two lockstep arrays means they cannot desynchronize.
        /// </summary>
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

        // Owner-thread fast flag for the "is this bus still usable" checks on the entry points. Not
        // the teardown trigger: the outermost dispatch frame decides from _closeState, which is the
        // properly synchronized field.
        private bool _disposed;

        // Thread-safe deferred-unsubscribe inbox. Foreign-thread callbacks enqueue here; the owner
        // thread applies the removals at its next entry point. The count exists so the hot
        // publish path pays one volatile read instead of touching the queue.
        private readonly ConcurrentQueue<IEventSubscription> _pendingRemovals =
            new ConcurrentQueue<IEventSubscription>();
        private int _pendingRemovalCount;

        private EventSubscription<T>[] _handlePool;
        private int _handlePoolCount;

        // Close protocol for the deferred-unsubscribe inbox (see ScheduleRemoval).
        //   0 = Open: foreign threads enqueue removals for the owner thread to apply.
        //   1 = DisposeRequested: a Dispose has been observed; teardown is running or waiting for
        //       an in-flight dispatch. Enqueue is still accepted so no handle is dropped, and the
        //       teardown performs a final drain.
        //   2 = Closed: teardown finished. A foreign thread must release synchronously instead of
        //       enqueueing, because nothing will ever drain the queue again.
        // The state is written with Interlocked/Volatile and re-checked after every enqueue so a
        // check-then-enqueue race cannot strand a subscription.
        private int _closeState;

        // Serializes "enqueue + re-check" against "publish Closed + final drain". Both sides drain,
        // so each alone leaves a window: the enqueuer can re-check before Closed is published, and
        // the closer can drain before the enqueue lands. Holding this gate across each side's
        // check-then-act removes the interleaving. It is never touched by Publish/Subscribe/Remove,
        // only by the foreign-thread cold path (ScheduleRemoval) and the one-shot teardown.
        private readonly object _closeGate = new object();

        // Exactly-once claim on TeardownCore: the outermost dispatch frame and a foreign-thread
        // Dispose can both decide to tear down, and the loser must not clear the handler array
        // underneath the winner. 0 = unclaimed, 1 = claimed.
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
        /// Slots to reserve up front. Sizing this to the expected steady-state subscriber count
        /// avoids every array growth on the subscribe path and removes a GC spike from warm-up.
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
        /// Dead slots left by unsubscribe. Compaction is automatic and bounded by the compaction
        /// ratio, so a value above zero is normal but never arbitrarily large; one that keeps
        /// climbing means subscribers are being added and removed faster than the ratio absorbs,
        /// which is a signal to gate the subscription with an activity flag instead of churning it.
        /// </summary>
        public int TombstoneCount => _tombstones;

        /// <summary>Total successful dispatch entries since construction.</summary>
        public long PublishCount => _publishCount;

        /// <summary>Publishes rejected because <see cref="DispatchDepth"/> hit the configured limit.</summary>
        public long DroppedReentrantCount => _droppedReentrantCount;

        /// <summary>Subscriber exceptions handled according to the configured policy.</summary>
        public long SubscriberErrorCount => _subscriberErrorCount;

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

            // Tombstone reuse is only safe outside dispatch: reusing a slot below the loop's count
            // snapshot would let a handler subscribed mid-round fire in that same round. The scan is
            // skipped when there is no tombstone, otherwise it walks the whole occupied range to no
            // effect before falling through to the append below.
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

        /// <summary>
        /// Pairs <paramref name="handler"/> with a freshly rented handle in <paramref name="index"/>.
        /// The handle must exist before the slot does, because the slot is what records it.
        /// </summary>
        private IEventSubscription PlaceHandler(Action<T> handler, int index)
        {
            EventSubscription<T> handle = RentHandle(handler, index);
            _handlers[index].Handler = handler;
            _handlers[index].Handle = handle;
            return handle;
        }

        /// <summary>
        /// Removes <paramref name="handler"/>; returns true when it was subscribed, false when it was
        /// not found. Unsubscribing a disposed bus is a no-op because disposal already dropped every
        /// handler, which keeps deferred scope disposal (a MonoBehaviour OnDestroy running after the
        /// context disposed the bus) safe.
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
        ///
        /// Zero allocation on the happy path. Under <see cref="PublishErrorPolicy.Stop"/> the first
        /// subscriber exception propagates and the remaining handlers are skipped. Under
        /// <see cref="PublishErrorPolicy.Swallow"/> every subscriber runs and failures are recorded.
        /// Under <see cref="PublishErrorPolicy.ContinueOnError"/> every subscriber runs and the first
        /// failure is rethrown once the round completes, so a fault can never be silently lost and
        /// can never cost another subscriber its delivery.
        /// </summary>
        public void Publish(in T evt)
        {
            if (_disposed)
            {
                ThrowDisposed();
            }

            // Apply deferred foreign-thread removals before the round snapshots its state.
            DrainPendingRemovals();

            // Re-entrancy ceiling. Dropping here is deliberate: an unbounded recursive publish chain
            // is a design error, and the alternative is a stack overflow that the runtime cannot
            // recover from on any platform. The drop is counted, logged and surfaced by diagnostics.
            if (_dispatchDepth >= _maxDispatchDepth)
            {
                _droppedReentrantCount++;

                // Counted is not enough on its own: the event reaches no subscriber and throws
                // nowhere, so the callsite has no other signal. Cold branch.
                LogDroppedReentrant();
                return;
            }

            _dispatchDepth++;
            _publishCount++;

            // Per-frame, not per-bus: a nested dispatch must not inherit or steal the fault captured
            // by the frame that called it.
            ExceptionDispatchInfo pendingFault = null;
            try
            {
                // The array is re-read every iteration on purpose. A subscriber that subscribes
                // during dispatch can replace the backing array; holding a stale local would keep
                // invoking handlers the bus has already forgotten.
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
                        // Routed unconditionally: `pendingFault ??= OnSubscriberException(...)`
                        // would short-circuit and silently drop every fault after the first, so a
                        // Swallow/ContinueOnError round would under-count. Only the *rethrow* keeps
                        // the first fault; logging and counting must see all of them.
                        ExceptionDispatchInfo fault = OnSubscriberException(exception);
                        pendingFault ??= fault;
                    }
                }

                if (pendingFault != null)
                {
                    // Rethrows with the original stack intact. The enclosing finally still runs, so
                    // dispatch depth is restored before the exception leaves the bus.
                    pendingFault.Throw();
                }
            }
            finally
            {
                // Depth is restored unconditionally: a subscriber that throws must not leave the bus
                // permanently unable to publish. Teardown and compaction are deferred to the
                // outermost frame so slot indices never shift underneath an in-flight iteration.
                if (--_dispatchDepth == 0)
                {
                    // The decision reads _closeState rather than _disposed because _closeState is the
                    // field Dispose and ScheduleRemoval exchange with Interlocked/Volatile, so a
                    // disposal requested from another thread cannot be missed here. Missing it would
                    // leave teardown unfinished and _closeState stuck at DisposeRequested, stranding
                    // every later removal in the inbox.
                    if (Volatile.Read(ref _closeState) != Open)
                    {
                        // Dispose was requested while this round was running. Replacing the array
                        // mid-round would make the loop read past the end, so the teardown the
                        // caller asked for happens here instead.
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
        /// Removes tombstone slots in place. Allocation-free and non-shrinking: capacity is retained
        /// so the steady state performs no array work at all.
        ///
        /// Automatic, so calling this directly is normally unnecessary. It exists for a one-shot
        /// reclaim after a burst of unsubscribes and for tests.
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
        /// Removes every subscription without disposing the bus, so the same instance can be reused
        /// across a scene or mode reset. Diagnostic counters are preserved because they describe the
        /// bus lifetime, not the current subscription set.
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
                _publishCount,
                _droppedReentrantCount,
                _subscriberErrorCount,
                _peakSubscriptionCount,
                _dispatchDepth,
                _handlers.Length,
                _disposed);
        }

        /// <summary>
        /// Tears the bus down. Idempotent. Called during an active dispatch round it is deferred:
        /// the round is atomic (its remaining handlers still run), <see cref="IsDisposed"/> turns
        /// true immediately so nested Publish and Subscribe throw, and the outermost dispatch frame
        /// performs the actual teardown on exit — replacing the handler array mid-round would make
        /// the dispatch loop read past the end.
        ///
        /// Owner thread only, like every other member. A foreign-thread call still completes, driven
        /// by the owner thread's outermost dispatch frame, but <see cref="ScheduleRemoval"/> is the
        /// only entry point a foreign thread needs.
        /// </summary>
        public void Dispose()
        {
            // Atomic close handshake: the first caller wins and every later call is a no-op, so
            // Dispose can never interleave with itself or leave the inbox half-open.
            if (Interlocked.CompareExchange(ref _closeState, DisposeRequested, Open) != Open)
            {
                return;
            }

            _disposed = true;

            if (_dispatchDepth > 0)
            {
                // An in-flight Publish is iterating the handler array right now; the outermost
                // dispatch frame tears the bus down on exit. TeardownCore re-checks this itself, so
                // a stale answer on a foreign thread cannot cause a bad clear.
                return;
            }

            TeardownCore();
        }

        /// <summary>
        /// Thread-safe deferred unsubscribe — the only sanctioned way to release a subscription
        /// from a thread that does not own this bus, e.g. a cancellation callback or a foreign
        /// completion source. The removal is applied on the owner thread at the next
        /// <see cref="Publish"/>, <see cref="Subscribe"/>, <see cref="Clear"/> or Dispose. Calling
        /// this from the owner thread is correct too; disposing the subscription directly is just
        /// cheaper there.
        /// </summary>
        public void ScheduleRemoval(IEventSubscription subscription)
        {
            if (subscription == null)
            {
                throw new ArgumentNullException(nameof(subscription));
            }

            lock (_closeGate)
            {
                // Already torn down: nothing will drain the inbox again, so release synchronously.
                // EventBus.Release short-circuits on a disposed bus and EventSubscription.Dispose is
                // idempotent, so this is a safe no-op rather than a handler-array mutation.
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

        /// <summary>
        /// Removals waiting in the deferred inbox. Non-zero after <see cref="Dispose"/> indicates a
        /// close-protocol leak; tests and the diagnostics surface assert on it.
        /// </summary>
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

            while (_pendingRemovals.TryDequeue(out IEventSubscription subscription))
            {
                Interlocked.Decrement(ref _pendingRemovalCount);

                // A subscription may be disposed directly as well (owner-thread double release);
                // Dispose is idempotent, so a duplicate here is harmless.
                subscription.Dispose();
            }
        }

        private void TeardownCore()
        {
            // Exactly-once claim: whoever arrives second must not clear the handler array underneath
            // the first.
            if (Interlocked.Exchange(ref _teardownClaimed, 1) != 0)
            {
                return;
            }

            // A dispatch may still be iterating. Clearing in place would start feeding that loop
            // nulled slots, cutting the round short in contradiction of the documented "the round is
            // atomic" guarantee; replacing the array would make it read past the end. Release the
            // claim so the outermost dispatch frame — which re-enters here with _dispatchDepth back
            // at zero — performs the clear. Only reachable when Dispose is called off the owner
            // thread, which the contract forbids.
            if (Volatile.Read(ref _dispatchDepth) > 0)
            {
                Interlocked.Exchange(ref _teardownClaimed, 0);
                return;
            }

            // Release the backing array so a retained (not-yet-collected) bus no longer pins a
            // large handler array, and drop the handle pool with it. A disposed bus cannot be
            // reused. Pending foreign-thread removals are drained so the inbox does not retain
            // them; their Dispose is a no-op on a disposed bus.
            DrainPendingRemovals();
            ClearCore();
            _handlers = Array.Empty<HandlerSlot>();
            _handlePool = null;
            _handlePoolCount = 0;

            // Publish Closed and run the final drain as one critical section against ScheduleRemoval:
            // either the enqueuer sees Open and this drain collects its entry, or it sees Closed and
            // releases the entry itself. No interleaving can leave an entry uncollected.
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

            // A negative slot means the slot was already reclaimed without this handle being disposed:
            // an identity-based Unsubscribe, Clear, or teardown. There is nothing left to remove, and
            // falling back to an identity scan could remove an unrelated subscription that happens to
            // share the delegate.
            if (slot >= 0)
            {
                // O(1) fast path. The slot records its own handle, and compaction re-stamps that
                // handle when it moves it, so a match proves the slot still belongs to this handle and
                // the removal needs no lookup. Handle identity is the whole guard: a stale index
                // cannot address a different subscriber's slot, because the handle in it would be a
                // different object. The scan is a defensive fallback only.
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
            // Hoisting the array is safe here: nothing in this method can re-enter the bus.
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

        /// <summary>
        /// Kills one live slot: detaches its handle, clears both fields, and runs the compaction
        /// threshold check. Shared by the identity-based <see cref="Unsubscribe"/> and the O(1)
        /// handle release so both maintain the same invariants.
        /// </summary>
        private void MarkSlotDead(int index)
        {
            _handlers[index].Handle.DetachSlot();
            _handlers[index].Handler = null;
            _handlers[index].Handle = null;
            _tombstones++;

            // Maintenance lives on the unsubscribe path, never on the publish path: unsubscribe
            // is the only thing that creates tombstones, so checking there costs one compare on a
            // cold path instead of one branch per dispatch.
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

        /// <summary>
        /// Compacts only on a proportional tombstone share, never on an absolute count. Compaction is
        /// O(_slots), so an absolute threshold would charge a full pass against a fixed number of
        /// unsubscribes and the amortized cost of an unsubscribe would grow with the subscriber
        /// count. Small buses need no floor: _slots / CompactRatio is already 2-3 at that size, so
        /// they compact immediately. A caller that wants a denser array than the ratio leaves should
        /// call <see cref="Compact"/> after a burst.
        /// </summary>
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

                // Moves the handle with its handler and re-stamps it, so the O(1) release path stays
                // valid across compaction. Compaction is the only thing that relocates live slots.
                EventSubscription<T> handle = handlers[readIndex].Handle;
                handle.MoveToSlot(writeIndex);
                handlers[writeIndex].Handler = handler;
                handlers[writeIndex].Handle = handle;
                writeIndex++;
            }

            // Clearing the tail is not cosmetic. Stale delegate references keep subscriber objects
            // alive; on destroyed MonoBehaviours that is a real leak the profiler will show as
            // managed heap that never comes back down.
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
                // Detach before dropping the reference: an outstanding handle that was never disposed
                // must not keep pointing at a slot that no longer belongs to it.
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

        /// <summary>
        /// Cold-path exception routing, kept out of line so the dispatch loop stays tight.
        ///
        /// Returns the fault to rethrow once the round finishes, or null when the policy swallows it.
        /// Under <see cref="PublishErrorPolicy.Stop"/> it rethrows here with the original stack
        /// preserved, which aborts the round and skips the remaining subscribers.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private ExceptionDispatchInfo OnSubscriberException(Exception exception)
        {
            _subscriberErrorCount++;
            LogException(exception);

            // ExceptionDispatchInfo is used instead of `throw exception` because rethrowing a caught
            // exception by value rewrites its stack trace and destroys the original throw site.
            ExceptionDispatchInfo captured = ExceptionDispatchInfo.Capture(exception);

            if (_publishErrorPolicy == PublishErrorPolicy.Stop)
            {
                captured.Throw();
            }

            return _publishErrorPolicy == PublishErrorPolicy.ContinueOnError ? captured : null;
        }

        /// <summary>
        /// Reports a publish refused by the re-entrancy ceiling. Cold path; the enable check runs
        /// first, so a disabled sink costs one call and the message is a constant.
        /// </summary>
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
            // No reflection: the delegate method name is deliberately omitted so the cold path stays
            // AOT-safe. The event type is already carried by the category.
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
            // The enable check must stay ahead of anything that allocates. Every message passed here
            // is a constant literal, so the enabled path allocates nothing either.
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
