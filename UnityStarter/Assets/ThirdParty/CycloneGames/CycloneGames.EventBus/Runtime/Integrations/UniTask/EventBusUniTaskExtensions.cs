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
        ///
        /// Must be called on the bus owner thread (in Unity, the main thread). Cancellation may
        /// come from any thread: the release is then deferred through the bus's thread-safe removal
        /// inbox, so the single-thread-confined bus is never mutated off the owner thread.
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
        /// Must be called on the bus owner thread; cancellation may come from any thread. A
        /// predicate that throws faults the wait with that exception and releases the subscription
        /// and the cancellation registration — the fault never reaches the bus dispatch, so a
        /// failing filter cannot silently park the waiter forever.
        ///
        /// If the bus is disposed while the wait is parked, the task never completes by event; the
        /// cancellation token is the escape hatch.
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
        ///
        /// Race protocol, in order: the cancellation registration is created first (so it exists
        /// before any settle path can run and can never be missed), the subscription second, then
        /// the settled flag is re-checked to close the window between the two, and a single
        /// try/finally disposes the registration on every exit.
        /// </summary>
        private sealed class EventAwaiter<T> where T : struct
        {
            private readonly EventBus<T> _bus;
            private readonly Func<T, bool> _predicate;
            private readonly Action<T> _handler;
            private readonly UniTaskCompletionSource<T> _completion = new UniTaskCompletionSource<T>();

            // The thread WaitAsync ran on — the bus owner thread by contract. The cancellation
            // callback compares against it to decide between a direct release and the deferred
            // thread-safe removal inbox.
            private readonly int _ownerThreadId;

            private IEventSubscription _subscription;
            private CancellationTokenRegistration _registration;
            private int _settled;

            internal EventAwaiter(EventBus<T> bus, Func<T, bool> predicate)
            {
                _bus = bus;
                _predicate = predicate;
                _ownerThreadId = Environment.CurrentManagedThreadId;

                // Cached once. Passing a method group to Subscribe would allocate a new delegate on
                // every call, because C# does not cache instance method group conversions.
                _handler = OnNext;
            }

            internal async Cysharp.Threading.Tasks.UniTask<T> Run(CancellationToken cancellationToken)
            {
                // Register BEFORE subscribing. Whatever happens next, the registration exists
                // before any settle path can run, so a settle can never miss it and leak it. A
                // token that is already cancelled fires OnCanceled synchronously inside Register —
                // with the subscription still null, which the paths below handle.
                if (cancellationToken.CanBeCanceled)
                {
                    _registration = cancellationToken.Register(OnCanceled);
                }

                try
                {
                    if (Volatile.Read(ref _settled) == 0)
                    {
                        _subscription = _bus.Subscribe(_handler);

                        if (Volatile.Read(ref _settled) != 0)
                        {
                            // Cancellation won the race between Register and Subscribe: OnCanceled
                            // saw a null subscription and scheduled nothing, so this owner-thread
                            // release is the only one.
                            ReleaseSubscription();
                        }
                    }

                    return await _completion.Task;
                }
                finally
                {
                    // The one unconditional release of the registration: event, cancellation and
                    // predicate fault all land here. Idempotent, and safe to call from the token's
                    // own callback.
                    _registration.Dispose();
                }
            }

            private void OnNext(T evt)
            {
                if (_predicate != null)
                {
                    bool matches;
                    try
                    {
                        matches = _predicate(evt);
                    }
                    catch (Exception exception)
                    {
                        // A throwing predicate must not propagate into the bus dispatch (the bus
                        // would record it as a subscriber fault and this waiter would park forever)
                        // and must not leave the subscription registered. We are inside a dispatch
                        // on the owner thread, so the direct release is safe.
                        if (Settle())
                        {
                            _completion.TrySetException(exception);
                        }

                        return;
                    }

                    if (!matches)
                    {
                        return;
                    }
                }

                if (Settle())
                {
                    _completion.TrySetResult(evt);
                }
            }

            private void OnCanceled()
            {
                if (Settle())
                {
                    _completion.TrySetCanceled();
                }
            }

            /// <summary>
            /// Claims the one-shot. Returns false when the other path already won, so a publish and
            /// a cancellation racing can never both complete the source.
            /// </summary>
            private bool Settle()
            {
                if (Interlocked.Exchange(ref _settled, 1) != 0)
                {
                    return false;
                }

                ReleaseSubscription();
                return true;
            }

            private void ReleaseSubscription()
            {
                IEventSubscription subscription = _subscription;
                _subscription = null;
                if (subscription == null)
                {
                    return;
                }

                if (Environment.CurrentManagedThreadId == _ownerThreadId)
                {
                    // Owner thread: the bus can be mutated directly.
                    subscription.Dispose();
                    return;
                }

                // The cancellation callback runs on whichever thread cancelled the token. The bus
                // is single-thread-confined, so from a foreign thread the unsubscribe is deferred
                // through the bus's thread-safe removal inbox and applied on the owner thread.
                _bus.ScheduleRemoval(subscription);
            }
        }
    }
}
