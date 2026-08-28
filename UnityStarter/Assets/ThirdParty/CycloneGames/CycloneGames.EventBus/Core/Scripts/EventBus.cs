using System;
using System.Text;

namespace CycloneGames.EventBus.Core
{
    /// <summary>
    /// Zero-allocation one-to-many notification bus. This is the hot path: <see cref="Publish"/> is
    /// synchronous, deterministic, and performs no managed allocation, no LINQ, no closure, no
    /// string, and no logging.
    ///
    /// Thread model: single-thread-confined. Subscribe, Unsubscribe, Publish, Compact, and Dispose
    /// must all run on one owner thread (Unity main thread). Confinement is the safety guarantee and
    /// the precondition for zero allocation, so <see cref="Publish"/> takes no lock.
    ///
    /// Structural changes during dispatch:
    /// - A handler subscribed during Publish never fires this round (it appends beyond the count
    ///   snapshot; tombstone reuse is suppressed while dispatch is in progress).
    /// - A handler unsubscribed during Publish is skipped once its slot is observed as null.
    /// </summary>
    public sealed class EventBus<T> : IDisposable, IEventBusDiagnostics where T : struct
    {
        private const int DefaultCapacity = 8;

        private readonly int _maxDispatchDepth;
        private readonly string _category;
        private readonly IEventBusLogSink _logSink;
        private readonly PublishErrorPolicy _publishErrorPolicy;

        private Action<T>[] _handlers;
        private int _slots;
        private int _tombstones;
        private int _dispatchDepth;
        private long _publishCount;
        private long _droppedReentrantCount;
        private bool _disposed;

        public EventBus(EventBusConfiguration configuration = null)
            : this(configuration, DefaultCapacity)
        {
        }

        public EventBus(EventBusConfiguration configuration, int initialCapacity)
        {
            configuration ??= EventBusConfiguration.Default;
            if (initialCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));
            }

            _maxDispatchDepth = configuration.MaxDispatchDepth;
            _logSink = configuration.LogSink ?? NullEventBusLogSink.Instance;
            _publishErrorPolicy = configuration.PublishErrorPolicy;
            _category = typeof(T).Name;
            _handlers = new Action<T>[initialCapacity];
        }

        public int SubscriptionCount => _slots - _tombstones;

        public int TombstoneCount => _tombstones;

        public long PublishCount => _publishCount;

        public bool IsDisposed => _disposed;

        public string EventTypeName => typeof(T).FullName;

        public EventSubscription Subscribe(Action<T> handler)
        {
            ThrowIfDisposed();
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            // Reuse a tombstone slot only when not dispatching; otherwise always append so a handler
            // subscribed mid-publish cannot appear at an index below the count snapshot.
            if (_dispatchDepth == 0)
            {
                for (int index = 0; index < _slots; index++)
                {
                    if (_handlers[index] == null)
                    {
                        _handlers[index] = handler;
                        _tombstones--;
                        LogSubscription("subscribed (reused tombstone slot)");
                        return new EventSubscription(() => Unsubscribe(handler));
                    }
                }
            }

            EnsureCapacity(_slots + 1);
            _handlers[_slots++] = handler;
            LogSubscription("subscribed");
            return new EventSubscription(() => Unsubscribe(handler));
        }

        /// <summary>
        /// Removes <paramref name="handler"/> and returns true when it was subscribed, or false when
        /// it was not found (or the bus is already disposed). Unsubscribing a disposed bus is a no-op
        /// because disposal has already dropped every handler; this keeps deferred scope disposal
        /// (for example a MonoBehaviour OnDestroy running after the context disposed the bus) safe.
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

            for (int index = 0; index < _slots; index++)
            {
                if (_handlers[index] == handler)
                {
                    _handlers[index] = null;
                    _tombstones++;
                    LogSubscription("unsubscribed");
                    return true;
                }
            }

            return false;
        }

        public void Publish(in T evt)
        {
            ThrowIfDisposed();

            if (_dispatchDepth >= _maxDispatchDepth)
            {
                _droppedReentrantCount++;
                return;
            }

            _dispatchDepth++;
            _publishCount++;
            try
            {
                int count = _slots;
                for (int index = 0; index < count; index++)
                {
                    Action<T> handler = _handlers[index];
                    if (handler == null)
                    {
                        continue;
                    }

                    // The exception filter runs only when a subscriber throws, so the hot path has
                    // zero overhead from the try/catch. In Stop mode the exception propagates; in
                    // Swallow mode it is logged (cold path) and dispatch continues.
                    try
                    {
                        handler(evt);
                    }
                    catch (Exception exception) when (_publishErrorPolicy == PublishErrorPolicy.Swallow)
                    {
                        LogException(exception);
                    }
                }
            }
            finally
            {
                _dispatchDepth--;
            }
        }

        /// <summary>
        /// Removes tombstone slots and shrinks storage. Cold path: only call when no dispatch is in
        /// progress and the tombstone count is high relative to live subscriptions.
        /// </summary>
        public void Compact()
        {
            ThrowIfDisposed();
            if (_dispatchDepth != 0)
            {
                throw new InvalidOperationException("Compact cannot run during dispatch.");
            }

            if (_tombstones == 0)
            {
                return;
            }

            int liveCount = _slots - _tombstones;
            int nextCapacity = Math.Max(DefaultCapacity, liveCount);
            var compacted = new Action<T>[nextCapacity];
            int writeIndex = 0;
            for (int readIndex = 0; readIndex < _slots; readIndex++)
            {
                Action<T> handler = _handlers[readIndex];
                if (handler != null)
                {
                    compacted[writeIndex++] = handler;
                }
            }

            _handlers = compacted;
            _slots = liveCount;
            _tombstones = 0;
            Log("compacted");
        }

        /// <summary>
        /// Removes every subscription without disposing the bus, so the same instance can be reused
        /// across a scene or mode reset. Diagnostic counters (PublishCount, DroppedReentrantCount) are
        /// preserved because they describe the bus lifetime, not the current subscription set.
        /// Like <see cref="Compact"/>, it must not run during dispatch.
        /// </summary>
        public void Clear()
        {
            ThrowIfDisposed();
            if (_dispatchDepth != 0)
            {
                throw new InvalidOperationException("Clear cannot run during dispatch.");
            }

            for (int index = 0; index < _slots; index++)
            {
                _handlers[index] = null;
            }

            _slots = 0;
            _tombstones = 0;
            Log("cleared");
        }

        public EventBusSnapshot GetSnapshot()
        {
            return new EventBusSnapshot(
                SubscriptionCount,
                TombstoneCount,
                _publishCount,
                _droppedReentrantCount,
                _dispatchDepth,
                _disposed);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            for (int index = 0; index < _slots; index++)
            {
                _handlers[index] = null;
            }

            _slots = 0;
            _tombstones = 0;

            // Release the backing array so a retained (not-yet-collected) bus no longer pins a large
            // handler array. A disposed bus cannot be reused, so an empty array is safe.
            _handlers = Array.Empty<Action<T>>();
            Log("disposed");
        }

        private void EnsureCapacity(int required)
        {
            if (required <= _handlers.Length)
            {
                return;
            }

            int nextCapacity = Math.Max(required, _handlers.Length * 2);
            var next = new Action<T>[nextCapacity];
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
            if (!_logSink.IsEnabled(EventBusLogSeverity.Debug, _category))
            {
                return;
            }

            string captured = message;
            _logSink.Write(EventBusLogSeverity.Debug, _category, builder => builder.Append(captured));
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(typeof(EventBus<T>).FullName);
            }
        }
    }
}
