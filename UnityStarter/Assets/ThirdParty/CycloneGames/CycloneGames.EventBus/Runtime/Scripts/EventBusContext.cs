using System;
using System.Collections.Generic;
using CycloneGames.EventBus.Core;

namespace CycloneGames.EventBus.Runtime
{
    /// <summary>
    /// The composition-root facade: one stable entry point for the game's notification buses, the
    /// command publisher, and the root subscription scope. It owns explicit disposal order — stop
    /// receiving, release subscriptions, then dispose child scopes and the command backend.
    ///
    /// Instances are single-thread-confined. There is deliberately no process-global singleton: the
    /// host owns a context instance (constructed manually or resolved from a DI container) and passes
    /// it where needed. That keeps dependencies explicit and testable.
    /// </summary>
    public sealed class EventBusContext : IDisposable
    {
        private readonly EventBusConfiguration _configuration;
        private readonly ICommandPublisher _commandPublisher;
        private readonly Dictionary<Type, IEventBusDiagnostics> _buses =
            new Dictionary<Type, IEventBusDiagnostics>();
        private readonly List<ISubscriptionScope> _scopes = new List<ISubscriptionScope>();
        private readonly SubscriptionScope _rootScope = new SubscriptionScope();

        private bool _disposed;

        public EventBusContext(EventBusConfiguration configuration, ICommandPublisher commandPublisher)
        {
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _commandPublisher = commandPublisher ?? throw new ArgumentNullException(nameof(commandPublisher));
        }

        public ICommandPublisher Commands => _commandPublisher;

        public ISubscriptionScope RootScope => _rootScope;

        public EventBusConfiguration Configuration => _configuration;

        /// <summary>
        /// Registers a caller-owned bus for <typeparamref name="T"/>. This is the DI-friendly entry
        /// point: a container (or manual composition root) constructs the bus and registers it here,
        /// so the context never creates buses behind the caller's back.
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

        /// <summary>
        /// Aggregates all registered buses into one fixed-size snapshot. Computing it is O(active
        /// buses) on the cold diagnostic path; each per-bus count is O(1).
        /// </summary>
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

            // 1. Stop receiving: dispose every bus (drops all handlers).
            foreach (KeyValuePair<Type, IEventBusDiagnostics> entry in _buses)
            {
                if (entry.Value is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }

            _buses.Clear();

            // 2. Release subscriptions, then child scopes.
            _rootScope.Dispose();
            for (int index = _scopes.Count - 1; index >= 0; index--)
            {
                _scopes[index].Dispose();
            }

            _scopes.Clear();

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
