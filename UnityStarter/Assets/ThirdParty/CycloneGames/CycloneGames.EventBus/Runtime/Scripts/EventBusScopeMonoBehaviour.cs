using System;
using CycloneGames.EventBus.Core;
using UnityEngine;

namespace CycloneGames.EventBus.Runtime
{
    /// <summary>
    /// Binds a subscription scope to a GameObject's lifetime. Attach this to a mode, window, or
    /// controller root and subscribe through <see cref="Scope"/>; every subscription is released when
    /// the GameObject is destroyed. Main-thread-only.
    /// </summary>
    public sealed class EventBusScopeMonoBehaviour : MonoBehaviour
    {
        private readonly SubscriptionScope _scope = new SubscriptionScope();

        public ISubscriptionScope Scope => _scope;

        public IEventSubscription Subscribe<T>(EventBus<T> bus, Action<T> handler) where T : struct
        {
            if (bus == null)
            {
                throw new ArgumentNullException(nameof(bus));
            }

            return _scope.Add(bus, handler);
        }

        private void OnDestroy()
        {
            _scope.Dispose();
        }
    }
}
