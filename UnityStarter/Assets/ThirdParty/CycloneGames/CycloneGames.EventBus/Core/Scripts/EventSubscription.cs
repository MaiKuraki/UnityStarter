using System;

namespace CycloneGames.EventBus.Core
{
    /// <summary>
    /// Non-generic handle returned by <see cref="EventBus{T}.Subscribe"/>. Disposing it releases the
    /// subscription exactly once; subsequent disposal is a no-op.
    ///
    /// The non-generic shape is deliberate: a scope, a command router, or an R3 bridge can aggregate
    /// subscriptions across many event types without knowing <c>T</c>.
    /// </summary>
    public interface IEventSubscription : IDisposable
    {
        /// <summary>True once <see cref="IDisposable.Dispose"/> has run. Disposal is idempotent.</summary>
        bool IsReleased { get; }
    }

    /// <summary>
    /// Subscription handle for <see cref="EventBus{T}"/>. It holds the bus, the handler, and the slot
    /// it occupies instead of an unsubscribe closure, so the owning bus can pool and reuse it: both
    /// subscribe and release are allocation-free and O(1) in steady state.
    ///
    /// Handles are single-thread-confined and owned by the bus that produced them. A handle is only
    /// valid while it is rented; the bus may pool and re-bind it, so never retain one after releasing
    /// it. Call <see cref="EventBus{T}.Subscribe"/> again instead.
    /// </summary>
    public sealed class EventSubscription<T> : IEventSubscription where T : struct
    {
        private EventBus<T> _bus;
        private Action<T> _handler;
        private int _slot;
        private bool _released;

        internal EventSubscription(EventBus<T> bus, Action<T> handler, int slot)
        {
            _bus = bus;
            _handler = handler;
            _slot = slot;
        }

        public bool IsReleased => _released;

        public void Dispose()
        {
            if (_released)
            {
                return;
            }

            _released = true;

            EventBus<T> bus = _bus;
            Action<T> handler = _handler;
            int slot = _slot;
            _bus = null;
            _handler = null;
            _slot = -1;

            // A bus that was already disposed drops its handler array, so there is nothing to
            // unsubscribe and nothing to pool. Releasing after disposal stays a silent no-op, which
            // keeps deferred teardown (a MonoBehaviour OnDestroy running after the context disposed
            // the bus) safe.
            bus?.Release(this, handler, slot);
        }

        internal void Reset(EventBus<T> bus, Action<T> handler, int slot)
        {
            _bus = bus;
            _handler = handler;
            _slot = slot;
            _released = false;
        }

        /// <summary>Re-stamps the slot after compaction moved this subscription.</summary>
        internal void MoveToSlot(int slot)
        {
            _slot = slot;
        }

        /// <summary>
        /// Marks the handle as no longer owning a slot. Called when the slot dies without the handle
        /// being disposed — an identity-based unsubscribe, a clear, or teardown — so a later
        /// <see cref="Dispose"/> cannot address a slot that has been reassigned.
        /// </summary>
        internal void DetachSlot()
        {
            _slot = -1;
        }
    }

    /// <summary>
    /// Wraps an arbitrary teardown callback as an <see cref="IEventSubscription"/>, so heterogeneous
    /// cleanups (R3 disposables, CancellationToken registrations, pooled-object returns) can live in
    /// an <see cref="ISubscriptionScope"/> alongside real bus subscriptions.
    ///
    /// This type is not pooled: it is for cold-path composition, not the subscribe/unsubscribe hot
    /// path.
    /// </summary>
    public sealed class CallbackSubscription : IEventSubscription
    {
        private Action _release;
        private bool _released;

        public CallbackSubscription(Action release)
        {
            _release = release ?? throw new ArgumentNullException(nameof(release));
        }

        public bool IsReleased => _released;

        public void Dispose()
        {
            if (_released)
            {
                return;
            }

            _released = true;

            Action release = _release;
            _release = null;
            release?.Invoke();
        }
    }
}
