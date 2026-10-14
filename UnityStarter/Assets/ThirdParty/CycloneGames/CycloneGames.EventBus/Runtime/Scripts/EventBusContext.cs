using System;
using System.Collections.Generic;
using CycloneGames.EventBus.Core;

namespace CycloneGames.EventBus.Runtime
{
    /// <summary>
    /// Composition-root facade: one entry point for the buses, the command publisher, and the root
    /// subscription scope, with explicit disposal order (subscriptions, child scopes, owned buses,
    /// command backend). Single-thread-confined; deliberately no process-global singleton — the
    /// host owns the instance and passes it where needed.
    /// </summary>
    public sealed class EventBusContext : IDisposable
    {
        private readonly EventBusConfiguration _configuration;
        private readonly ICommandPublisher _commandPublisher;
        private readonly Dictionary<Type, IEventBusDiagnostics> _buses =
            new Dictionary<Type, IEventBusDiagnostics>();
        // Event types whose bus this context owns and must dispose. RegisterBus adopts caller-owned
        // buses and deliberately leaves them out; GetOrCreateBus and RegisterOwnedBus add to it.
        private readonly HashSet<Type> _ownedBuses = new HashSet<Type>();
        private readonly List<ISubscriptionScope> _scopes = new List<ISubscriptionScope>();
        private readonly SubscriptionScope _rootScope = new SubscriptionScope();

        private bool _disposed;

        public EventBusContext(EventBusConfiguration configuration, ICommandPublisher commandPublisher)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _commandPublisher = commandPublisher ?? throw new ArgumentNullException(nameof(commandPublisher));
        }

        /// <summary>
        /// True after <see cref="Dispose"/>. Tooling checks this to stop observing a dead context:
        /// its snapshot is legitimately all zeros.
        /// </summary>
        public bool IsDisposed => _disposed;

        public ICommandPublisher Commands => _commandPublisher;

        public ISubscriptionScope RootScope => _rootScope;

        public EventBusConfiguration Configuration => _configuration;

        /// <summary>
        /// Registers a caller-owned bus for <typeparamref name="T"/> (DI-friendly). The context
        /// does NOT dispose it; use <see cref="RegisterOwnedBus{T}"/> to transfer ownership, or
        /// <see cref="GetOrCreateBus{T}"/> to let the context create and own one.
        /// </summary>
        public void RegisterBus<T>(EventBus<T> bus) where T : struct
        {
            ThrowIfDisposed();
            if (bus == null)
            {
                throw new ArgumentNullException(nameof(bus));
            }

            Type type = typeof(T);
            if (_buses.ContainsKey(type))
            {
                throw new InvalidOperationException(
                    $"A bus for '{type.FullName}' is already registered.");
            }

            _buses.Add(type, bus);
        }

        /// <summary>
        /// Registers a bus for <typeparamref name="T"/> and transfers ownership to this context:
        /// <see cref="Dispose"/> will dispose it.
        /// </summary>
        public void RegisterOwnedBus<T>(EventBus<T> bus) where T : struct
        {
            RegisterBus<T>(bus);
            _ownedBuses.Add(typeof(T));
        }

        /// <summary>
        /// Returns the registered bus for <typeparamref name="T"/>, or null when none has been
        /// registered via <see cref="RegisterBus{T}"/>.
        /// </summary>
        public EventBus<T> GetBus<T>() where T : struct
        {
            ThrowIfDisposed();
            if (_buses.TryGetValue(typeof(T), out IEventBusDiagnostics existing))
            {
                return (EventBus<T>)existing;
            }

            return null;
        }

        /// <summary>
        /// Non-DI convenience: returns the bus for <typeparamref name="T"/>, creating and registering
        /// it on first access using the context configuration. Prefer <see cref="RegisterBus{T}"/>
        /// when a DI container owns the bus lifetime.
        /// </summary>
        public EventBus<T> GetOrCreateBus<T>() where T : struct
        {
            ThrowIfDisposed();
            EventBus<T> existing = GetBus<T>();
            if (existing != null)
            {
                return existing;
            }

            var bus = new EventBus<T>(_configuration);
            _buses.Add(typeof(T), bus);
            _ownedBuses.Add(typeof(T));
            return bus;
        }

        /// <summary>Creates a tracked child scope; the context disposes it on <see cref="Dispose"/>.</summary>
        public ISubscriptionScope CreateScope()
        {
            ThrowIfDisposed();
            var scope = new SubscriptionScope();
            _scopes.Add(scope);
            return scope;
        }

        /// <summary>Aggregates all registered buses into one fixed-size snapshot.</summary>
        public EventBusDiagnosticsSnapshot GetDiagnosticsSnapshot()
        {
            int subscriptionCount = 0;
            int tombstoneCount = 0;
            long publishCount = 0;
            long droppedReentrantCount = 0;
            long subscriberErrorCount = 0;
            int peakSubscriptionCount = 0;
            foreach (KeyValuePair<Type, IEventBusDiagnostics> entry in _buses)
            {
                EventBusSnapshot snapshot = entry.Value.GetSnapshot();
                subscriptionCount += snapshot.SubscriptionCount;
                tombstoneCount += snapshot.TombstoneCount;
                publishCount += snapshot.PublishCount;
                droppedReentrantCount += snapshot.DroppedReentrantCount;
                subscriberErrorCount += snapshot.SubscriberErrorCount;

                // Peak is a max, not a sum: summing it would report a number no bus ever reached and
                // would overstate the capacity a host should reserve.
                if (snapshot.PeakSubscriptionCount > peakSubscriptionCount)
                {
                    peakSubscriptionCount = snapshot.PeakSubscriptionCount;
                }
            }

            return new EventBusDiagnosticsSnapshot(
                _buses.Count,
                _scopes.Count + 1,
                subscriptionCount,
                tombstoneCount,
                publishCount,
                droppedReentrantCount,
                subscriberErrorCount,
                peakSubscriptionCount);
        }

        /// <summary>Copies the registered bus type names and per-bus snapshots for tooling.</summary>
        public IReadOnlyList<IEventBusDiagnostics> GetRegisteredBuses()
        {
            var result = new List<IEventBusDiagnostics>(_buses.Count);
            foreach (KeyValuePair<Type, IEventBusDiagnostics> entry in _buses)
            {
                result.Add(entry.Value);
            }

            return result;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            // 1. Release subscriptions and child scopes first: disposing a scope unsubscribes, and
            //    that must happen while the buses are still alive.
            _rootScope.Dispose();
            for (int index = _scopes.Count - 1; index >= 0; index--)
            {
                _scopes[index].Dispose();
            }

            _scopes.Clear();

            // 2. Dispose only the buses this context owns. A bus registered through RegisterBus is
            //    caller-owned and outlives the context by contract, so it is left running.
            foreach (KeyValuePair<Type, IEventBusDiagnostics> entry in _buses)
            {
                if (_ownedBuses.Contains(entry.Key) && entry.Value is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }

            _ownedBuses.Clear();
            _buses.Clear();

            // 3. Release the command backend last.
            if (_commandPublisher is IDisposable disposablePublisher)
            {
                disposablePublisher.Dispose();
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(EventBusContext));
            }
        }
}
}
