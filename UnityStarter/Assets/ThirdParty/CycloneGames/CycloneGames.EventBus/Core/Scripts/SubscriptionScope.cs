using System;
using System.Collections.Generic;

namespace CycloneGames.EventBus.Core
{
    /// <summary>
    /// Concrete subscription scope. Cold-path container: the list grows only on subscribe, and
    /// <see cref="Dispose"/> is idempotent. It is single-thread-confined like the buses it collects.
    /// </summary>
    public sealed class SubscriptionScope : ISubscriptionScope
    {
        private List<IEventSubscription> _subscriptions;
        private bool _disposed;

        public int Count => _subscriptions?.Count ?? 0;

        public void Add(IEventSubscription subscription)
        {
            ThrowIfDisposed();
            if (subscription == null)
            {
                throw new ArgumentNullException(nameof(subscription));
            }

            if (_subscriptions == null)
            {
                _subscriptions = new List<IEventSubscription>();
            }

            _subscriptions.Add(subscription);
        }

        /// <summary>
        /// Subscribes to a bus and records the returned handle in this scope. Convenience for the
        /// common "subscribe and forget into this scope" pattern.
        /// </summary>
        public IEventSubscription Add<T>(EventBus<T> bus, Action<T> handler) where T : struct
        {
            if (bus == null)
            {
                throw new ArgumentNullException(nameof(bus));
            }

            IEventSubscription subscription = bus.Subscribe(handler);
            Add(subscription);
            return subscription;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            if (_subscriptions == null)
            {
                return;
            }

            for (int index = _subscriptions.Count - 1; index >= 0; index--)
            {
                _subscriptions[index].Dispose();
            }

            _subscriptions.Clear();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(SubscriptionScope));
            }
        }
    }
}
