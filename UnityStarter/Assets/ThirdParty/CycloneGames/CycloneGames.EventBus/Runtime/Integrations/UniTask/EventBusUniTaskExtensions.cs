using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using CycloneGames.EventBus.Core;

// The trailing namespace segment is intentionally spelled `UniTask`, which is also a type name in
// Cysharp.Threading.Tasks. Inside this namespace the bare identifier `UniTask` binds to the
// namespace, so every Cysharp UniTask type that appears in a signature is written fully qualified.
// `UniTaskCompletionSource<T>` has no collision and is used unqualified.
namespace CycloneGames.EventBus.Runtime.Integrations.UniTask
{
    /// <summary>
    /// Awaits a single event from a Core <see cref="EventBus{T}"/>. UniTask types appear only in this
    /// integration assembly; the Core layer never depends on UniTask.
    ///
    /// This is the one place where a bus is consumed asynchronously, and the shape is deliberately
    /// narrow: wait for one event, with an optional filter and a cancellation token. There is no
    /// async enumerable over a bus, because a bus has no completion and no backpressure — an open
    /// async stream over one is an unbounded subscription that most callers forget to dispose.
    ///
    /// Cost: one subscription handle (pooled by the bus) plus one completion source per wait. This is
    /// a gameplay/sequencing construct, not a per-frame path; do not use it in a tight loop.
    /// </summary>
    public static class EventBusUniTaskExtensions
    {
        /// <summary>
        /// Completes with the next event published on <paramref name="bus"/>.
        /// </summary>
        public static Cysharp.Threading.Tasks.UniTask<T> WaitAsync<T>(
            this EventBus<T> bus,
            CancellationToken cancellationToken = default)
            where T : struct
        {
            return WaitAsync(bus, null, cancellationToken);
        }

        /// <summary>
        /// Completes with the next event published on <paramref name="bus"/> that satisfies
        /// <paramref name="predicate"/>. A null predicate accepts the next event of any value.
        ///
        /// The subscription is released as soon as the wait completes, on either the event or the
        /// cancellation path, so a cancelled wait leaves no handler behind.
        /// </summary>
        public static Cysharp.Threading.Tasks.UniTask<T> WaitAsync<T>(
            this EventBus<T> bus,
            Func<T, bool> predicate,
            CancellationToken cancellationToken = default)
            where T : struct
        {
            if (bus == null)
            {
                throw new ArgumentNullException(nameof(bus));
            }

            return new EventAwaiter<T>(bus, predicate).Run(cancellationToken);
        }

        /// <summary>
        /// One-shot waiter. Separated into its own type so the subscription handle, the completion
        /// source and the cancellation registration have one owner with one release path — releasing
        /// any of them independently is how these helpers leak handlers.
        /// </summary>
        private sealed class EventAwaiter<T> where T : struct
        {
            private readonly EventBus<T> _bus;
            private readonly Func<T, bool> _predicate;
            private readonly Action<T> _handler;
            private readonly UniTaskCompletionSource<T> _completion = new UniTaskCompletionSource<T>();

            private IEventSubscription _subscription;
            private CancellationTokenRegistration _registration;
            private int _settled;

            internal EventAwaiter(EventBus<T> bus, Func<T, bool> predicate)
            {
                _bus = bus;
                _predicate = predicate;

                // Cached once. Passing a method group to Subscribe would allocate a new delegate on
                // every call, because C# does not cache instance method group conversions.
                _handler = OnNext;
            }

            internal Cysharp.Threading.Tasks.UniTask<T> Run(CancellationToken cancellationToken)
            {
                _subscription = _bus.Subscribe(_handler);

                if (cancellationToken.IsCancellationRequested)
                {
                    // Subscribed first, then settled: the release path is the same either way, so
                    // there is no window where a later publish finds a half-torn-down waiter.
                    Settle();
                    _completion.TrySetCanceled(cancellationToken);
                    return _completion.Task;
                }

                if (cancellationToken.CanBeCanceled)
                {
                    _registration = cancellationToken.Register(OnCanceled);
                }

                return _completion.Task;
            }

            private void OnNext(T evt)
            {
                if (_predicate != null && !_predicate(evt))
                {
                    return;
                }

                if (!Settle())
                {
                    return;
                }

                _completion.TrySetResult(evt);
            }

            private void OnCanceled()
            {
                if (!Settle())
                {
                    return;
                }

                _completion.TrySetCanceled();
            }

            /// <summary>
            /// Claims the one-shot. Returns false when the other path already won, so a publish and a
            /// cancellation racing can never both complete the source.
            /// </summary>
            private bool Settle()
            {
                if (Interlocked.Exchange(ref _settled, 1) != 0)
                {
                    return false;
                }

                // Disposing during a dispatch is safe: the bus defers compaction to the outermost
                // dispatch frame, so slot indices never shift under the active iteration.
                _subscription?.Dispose();
                _subscription = null;

                // Safe from inside the callback: CancellationTokenRegistration.Dispose is explicitly
                // documented not to deadlock when called from the token's own callback.
                _registration.Dispose();
                return true;
            }
        }
    }
}
