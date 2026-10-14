using System;

namespace CycloneGames.EventBus.Core
{
    /// <summary>
    /// Non-generic handle returned by <see cref="EventBus{T}.Subscribe"/>. Disposal is idempotent.
    /// The non-generic shape lets scopes and bridges aggregate subscriptions across event types.
    /// </summary>
    public interface IEventSubscription : IDisposable
    {
        /// <summary>True once <see cref="IDisposable.Dispose"/> has run. Disposal is idempotent.</summary>
        bool IsReleased { get; }
    }

    /// <summary>
    /// Subscription handle for <see cref="EventBus{T}"/>. Holds (bus, handler, slot) instead of an
    /// unsubscribe closure so the bus can pool it: subscribe and release are allocation-free and
    /// O(1) in steady state. Single-thread-confined; a released handle may be re-bound by the pool,
    /// so never retain one after release — call Subscribe again.
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

        // Marks the handle as no longer owning a slot (identity unsubscribe, clear, or teardown),
        // so a later Dispose cannot address a reassigned slot.
        internal void DetachSlot()
        {
            _slot = -1;
        }
    }

    /// <summary>
    /// Wraps an arbitrary teardown callback as an <see cref="IEventSubscription"/> so heterogeneous
    /// cleanups can live in an <see cref="ISubscriptionScope"/> alongside bus subscriptions.
    /// Not pooled: cold-path composition only.
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
