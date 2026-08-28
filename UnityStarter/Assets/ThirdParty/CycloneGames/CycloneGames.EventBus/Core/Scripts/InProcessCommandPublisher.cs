using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CycloneGames.EventBus.Core
{
    /// <summary>
    /// Synchronous, single-thread-confined command publisher used when VitalRouter is not wired.
    /// A command is dispatched immediately; if a handler is already running (re-entrant publish), the
    /// command is placed in a bounded queue and drained in order after the running handler completes.
    /// Overflow beyond the capacity applies <see cref="CommandOverflowPolicy"/>, so the queue never
    /// grows without bound.
    ///
    /// Command payloads are boxed into the queue closures, which is acceptable because the command
    /// path is not the zero-allocation hot path — that is <see cref="EventBus{T}"/>. Use the
    /// VitalRouter backend for zero-allocation command routing.
    /// </summary>
    public sealed class InProcessCommandPublisher : ICommandPublisher, IDisposable
    {
        private readonly Dictionary<Type, Delegate> _handlers = new Dictionary<Type, Delegate>();
        private readonly int _capacity;
        private readonly CommandOverflowPolicy _overflowPolicy;
        private readonly Queue<Func<ValueTask>> _pending = new Queue<Func<ValueTask>>();

        private bool _running;
        private bool _disposed;

        public InProcessCommandPublisher(
            int capacity = EventBusConfiguration.DefaultCommandQueueCapacity,
            CommandOverflowPolicy overflowPolicy = CommandOverflowPolicy.Drop)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _capacity = capacity;
            _overflowPolicy = overflowPolicy;
        }

        public int PendingCommandCount => _pending.Count;

        public void RegisterHandler<TCommand>(Func<TCommand, CancellationToken, ValueTask> handler)
            where TCommand : struct
        {
            ThrowIfDisposed();
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            Type commandType = typeof(TCommand);
            if (_handlers.ContainsKey(commandType))
            {
                throw new InvalidOperationException(
                    $"A handler for '{commandType.FullName}' is already registered.");
            }

            _handlers[commandType] = handler;
        }

        /// <summary>
        /// Registers a synchronous handler. Convenience for handlers that do not await.
        /// </summary>
        public void RegisterHandler<TCommand>(Action<TCommand> handler) where TCommand : struct
        {
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            RegisterHandler<TCommand>((command, cancellationToken) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                handler(command);
                return default;
            });
        }

        public ValueTask PublishAsync<TCommand>(
            in TCommand command,
            CancellationToken cancellationToken = default)
            where TCommand : struct
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            // Copy the `in` parameter to a local: an async method cannot declare `in`, so the body
            // lives in a separate async method that takes the struct by value.
            TCommand captured = command;
            return PublishCoreAsync(captured, cancellationToken);
        }

        private async ValueTask PublishCoreAsync<TCommand>(
            TCommand command,
            CancellationToken cancellationToken)
            where TCommand : struct
        {
            if (!_handlers.TryGetValue(typeof(TCommand), out Delegate registered))
            {
                return;
            }

            var handler = (Func<TCommand, CancellationToken, ValueTask>)registered;

            if (_running)
            {
                if (_pending.Count >= _capacity)
                {
                    ApplyOverflow(cancellationToken);
                    return;
                }

                _pending.Enqueue(() => handler(command, cancellationToken));
                return;
            }

            _running = true;
            try
            {
                // Deliberately no ConfigureAwait(false): the publisher is single-thread-confined, so
                // the drain loop and the _running flag must resume on the owner thread.
                await handler(command, cancellationToken);

                while (_pending.Count > 0)
                {
                    Func<ValueTask> work = _pending.Dequeue();
                    await work();
                }
            }
            finally
            {
                _running = false;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _handlers.Clear();
            _pending.Clear();
        }

        private void ApplyOverflow(CancellationToken cancellationToken)
        {
            if (_overflowPolicy == CommandOverflowPolicy.FailFast)
            {
                throw new InvalidOperationException(
                    "InProcessCommandPublisher queue exceeded its bounded capacity.");
            }

            // Drop policy: the command is silently discarded. The caller's task completes normally.
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(InProcessCommandPublisher));
            }
        }
    }
}
