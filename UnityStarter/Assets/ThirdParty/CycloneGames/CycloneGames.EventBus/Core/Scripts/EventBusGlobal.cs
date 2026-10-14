using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace CycloneGames.EventBus.Core
{
    /// <summary>
    /// Per-event-type static holder for hosts without a DI container or an EventBusContext.
    /// A generic static field read is a single load — no hashing, no cast — so publishing through
    /// this holder costs the same as through a directly held reference.
    ///
    /// This is a service locator: dependencies become implicit and tests must reset state. Prefer
    /// constructor injection or an EventBusContext where a composition root exists.
    /// Single-thread-confined like the buses it points at; assign at startup, clear at teardown.
    /// </summary>
    public static class EventBusGlobal<T> where T : struct
    {
        private static EventBus<T> _instance;

        /// <summary>The bus for <typeparamref name="T"/>, or null when none has been assigned.</summary>
        public static EventBus<T> Instance
        {
            get => Volatile.Read(ref _instance);
            // Volatile write to match the atomic exchange in ClearIf.
            set => Volatile.Write(ref _instance, value);
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
