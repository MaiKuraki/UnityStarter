using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace CycloneGames.EventBus.Core
{
    /// <summary>
    /// Per-event-type static holder, for hosts that do not use a DI container or an
    /// <see cref="EventBusContext"/>.
    ///
    /// Resolving a bus through a <c>Dictionary&lt;Type, ...&gt;</c> per publish costs roughly 3 ns on
    /// desktop x64 and measurably more on mobile, which is comparable to the entire dispatch of a
    /// small event. A generic static field read is a single load with no hashing and no cast, so
    /// publishing through this holder costs the same as publishing through a directly held
    /// reference.
    ///
    /// This is a service locator, and it carries the usual trade-off: dependencies become implicit
    /// and tests must reset state. It exists because the alternative for non-DI code is worse, not
    /// because it is the preferred shape. Prefer constructor injection
    /// (<c>EventBus&lt;T&gt;</c> as a constructor parameter) or an <see cref="EventBusContext"/> where
    /// a composition root exists.
    ///
    /// Assign holders once at startup and clear them during teardown. Single-thread-confined like
    /// the buses they point at.
    /// </summary>
    public static class EventBusGlobal<T> where T : struct
    {
        private static EventBus<T> _instance;

        /// <summary>The bus for <typeparamref name="T"/>, or null when none has been assigned.</summary>
        public static EventBus<T> Instance
        {
            get => _instance;
            set => _instance = value;
        }

        public static bool HasInstance => _instance != null;

        /// <summary>Publishes through <see cref="Instance"/>. Allocation-free.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Publish(in T evt)
        {
            EventBus<T> bus = _instance;
            if (bus == null)
            {
                ThrowNotAssigned();
            }

            bus.Publish(evt);
        }

        /// <summary>Drops the holder. Call during teardown so the next run starts clean.</summary>
        public static void Clear()
        {
            _instance = null;
        }

        /// <summary>
        /// Drops the holder only when it still reference-equals <paramref name="expected"/>. Without
        /// this, a teardown that runs late can clear a bus the next context has already installed.
        /// Returns true when the holder was cleared.
        /// </summary>
        public static bool ClearIf(EventBus<T> expected)
        {
            if (expected == null)
            {
                throw new ArgumentNullException(nameof(expected));
            }

            return Interlocked.CompareExchange(ref _instance, null, expected) == expected;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowNotAssigned()
        {
            throw new InvalidOperationException(
                $"No EventBus<{typeof(T).Name}> was assigned to EventBusGlobal. Assign it at the "
                + "composition root before publishing, or inject the bus explicitly.");
        }
    }
}
