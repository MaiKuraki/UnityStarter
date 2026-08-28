using System;
using R3;
using CycloneGames.EventBus.Core;

namespace CycloneGames.EventBus.Runtime.Integrations.R3
{
    /// <summary>
    /// Bridges a Core <see cref="EventBus{T}"/> to an R3 <see cref="Observable{T}"/> and back.
    /// R3 types appear only in this integration assembly; the Core layer never depends on R3.
    /// Both directions allocate only on the cold subscribe/unsubscribe path.
    /// </summary>
    public static class EventBusObservableExtensions
    {
        /// <summary>
        /// Exposes a bus as an observable. Every publish is forwarded to subscribed observers;
        /// disposing the observable subscription unsubscribes from the bus.
        /// </summary>
        public static Observable<T> ToObservable<T>(this EventBus<T> bus) where T : struct
        {
            if (bus == null)
            {
                throw new ArgumentNullException(nameof(bus));
            }

            return Observable.Create<T>(observer =>
            {
                IEventSubscription subscription = bus.Subscribe(evt => observer.OnNext(evt));
                return subscription;
            });
        }

        /// <summary>
        /// Subscribes an observable into a bus: each source value is published to the bus.
        /// </summary>
        public static IEventSubscription SubscribeTo<T>(this EventBus<T> bus, Observable<T> source)
            where T : struct
        {
            if (bus == null)
            {
                throw new ArgumentNullException(nameof(bus));
            }

            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            IDisposable sourceSubscription = source.Subscribe(evt => bus.Publish(evt));
            return new EventSubscription(sourceSubscription.Dispose);
        }
    }
}
