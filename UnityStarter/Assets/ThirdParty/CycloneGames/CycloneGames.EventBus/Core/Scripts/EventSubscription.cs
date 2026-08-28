using System;
using System.Threading;

namespace CycloneGames.EventBus.Core
{
    /// <summary>
    /// Non-generic handle returned by <see cref="EventBus{T}.Subscribe"/>. Disposing it unsubscribes
    /// the handler exactly once; subsequent disposal is a no-op.
    /// </summary>
    public interface IEventSubscription : IDisposable
    {
    }

    /// <summary>
    /// A single subscription handle. It owns no state beyond the unsubscribe callback, so scopes can
    /// aggregate heterogeneous subscriptions without knowing the event type.
    /// </summary>
    public sealed class EventSubscription : IEventSubscription
    {
        private Action _unsubscribe;
        private int _disposed;

        public EventSubscription(Action unsubscribe)
        {
            _unsubscribe = unsubscribe ?? throw new ArgumentNullException(nameof(unsubscribe));
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            Action unsubscribe = _unsubscribe;
            _unsubscribe = null;
            unsubscribe();
        }
    }
}
