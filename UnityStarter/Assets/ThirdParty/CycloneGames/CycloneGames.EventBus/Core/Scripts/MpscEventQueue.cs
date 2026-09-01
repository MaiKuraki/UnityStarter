using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace CycloneGames.EventBus.Core
{
    /// <summary>
    /// Multi-producer, single-consumer (MPSC) bridge for crossing a thread boundary into a
    /// main-thread-confined <see cref="EventBus{T}"/>. Any number of background threads enqueue;
    /// exactly one owner thread drains into the bus.
    ///
    /// This is the only supported way to reach a bus from background work (networking, asset
    /// loading, job completion callbacks). The bus itself stays single-thread-confined and lock-free
    /// because only the owner thread ever touches it.
    ///
    /// Implementation: a bounded Vyukov-style array queue with a per-slot sequence number.
    /// - Allocation-free on both sides. Storage is reserved at construction; there is no per-event
    ///   node, which is what a <c>ConcurrentQueue&lt;T&gt;</c> would allocate (~21 bytes per event
    ///   measured on desktop x64, and it is exactly the kind of steady drip that shows up as GC
    ///   spikes on mobile).
    /// - Bounded. <see cref="TryEnqueue"/> returns false when full and counts the drop instead of
    ///   growing, so a stalled consumer produces backpressure the producer can observe rather than
    ///   unbounded memory growth.
    /// - Lock-free and wait-free on the consumer side. Producers retry on contention; nothing blocks
    ///   and nothing allocates.
    ///
    /// Ordering: events enqueued by one producer are drained in that producer's order. Across
    /// producers there is no global order, by definition of concurrent enqueue.
    ///
    /// Threading primitives are limited to <see cref="Interlocked"/> and <see cref="Volatile"/>,
    /// which behave identically on every Unity backend, including single-threaded WebGL where the
    /// barriers compile away and the queue degenerates to a plain FIFO.
    /// </summary>
    /// <summary>
    /// Cache-line padding for the two ring positions. The producer index is hammered by every
    /// producer thread while the consumer index is written by the owner thread; without separation
    /// they share a line and every successful enqueue invalidates the consumer's cache line.
    ///
    /// Declared at namespace scope on purpose: a struct nested inside a generic type is itself
    /// generic, and the CLR rejects explicit layout on generic types (TypeLoadException at first
    /// use on CoreCLR, and an AOT-time failure under IL2CPP). Struct field layout within a class
    /// is not formally guaranteed either, so this is a strong hint rather than a hard guarantee.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    internal struct PaddedInt
    {
        [FieldOffset(0)]
        public int Value;
    }

    public sealed class MpscEventQueue<T> where T : struct
    {
        private struct Slot
        {
            public T Data;
            public int Sequence;
        }

        private readonly Slot[] _slots;
        private readonly int _mask;
        private PaddedInt _enqueuePos;
        private PaddedInt _dequeuePos;
        private long _droppedCount;
        private long _rejectedCount;

        /// <param name="capacity">
        /// Maximum number of pending events. Rounded up to the next power of two so the index wrap is
        /// a mask instead of a modulo.
        /// </param>
        public MpscEventQueue(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            int powerOfTwo = 1;
            while (powerOfTwo < capacity)
            {
                powerOfTwo <<= 1;
                if (powerOfTwo <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(capacity));
                }
            }

            _slots = new Slot[powerOfTwo];
            _mask = powerOfTwo - 1;

            // Slot i starts publishable for enqueue position i, which is what lets the first lap
            // through the ring work without a special case.
            for (int index = 0; index < powerOfTwo; index++)
            {
                _slots[index].Sequence = index;
            }
        }

        /// <summary>Fixed event capacity, after power-of-two rounding.</summary>
        public int Capacity => _slots.Length;

        /// <summary>
        /// Events that entered the queue and were then discarded unread by <see cref="Clear"/>.
        /// Thread-safe read.
        ///
        /// This counts confirmed loss only. A write that the queue refused is not a loss: the caller
        /// may retry it, and <see cref="TryEnqueue"/> cannot know the caller's intent. That case is
        /// <see cref="RejectedCount"/>.
        /// </summary>
        public long DroppedCount => Interlocked.Read(ref _droppedCount);

        /// <summary>
        /// Write attempts refused because the queue was full. Thread-safe read.
        ///
        /// This is the producer-pressure signal: it climbs when the consumer cannot keep up. It is
        /// not a loss count — a caller that retries on <c>false</c> loses nothing and still
        /// increments this. Count your own losses if you drop on <c>false</c>.
        /// </summary>
        public long RejectedCount => Interlocked.Read(ref _rejectedCount);

        /// <summary>Approximate pending count. Safe from any thread; treat it as advisory.</summary>
        public int PendingCount
        {
            get
            {
                // Positions are never more than Capacity apart, so a plain int difference cannot
                // wrap into a misleading value. Casting to uint first would wrap instead of going
                // negative, which is the opposite of what a stalled-consumer check wants.
                int pending = unchecked(Volatile.Read(ref _enqueuePos.Value) - Volatile.Read(ref _dequeuePos.Value));
                return pending < 0 ? 0 : pending;
            }
        }

        /// <summary>True when no event is pending. Safe from any thread; treat it as advisory.</summary>
        public bool IsEmpty => PendingCount == 0;

        /// <summary>
        /// Enqueues from any thread. Returns false when the queue is full; the event is then the
        /// caller's to retry, drop, or count. Never allocates, never blocks.
        /// </summary>
        public bool TryEnqueue(in T evt)
        {
            Slot[] slots = _slots;
            int mask = _mask;

            while (true)
            {
                int pos = Volatile.Read(ref _enqueuePos.Value);
                ref Slot slot = ref slots[pos & mask];
                int sequence = Volatile.Read(ref slot.Sequence);

                // All comparisons are relative differences in unchecked int arithmetic, so the
                // counter wrapping past int.MaxValue is harmless: positions and slot sequences
                // advance in lockstep.
                int diff = unchecked(sequence - pos);

                if (diff == 0)
                {
                    if (Interlocked.CompareExchange(ref _enqueuePos.Value, pos + 1, pos) == pos)
                    {
                        // The release store on Sequence publishes the data written above it; the
                        // consumer's acquire read below is what makes it visible.
                        slot.Data = evt;
                        Volatile.Write(ref slot.Sequence, unchecked(pos + 1));
                        return true;
                    }

                    // Lost the race for this position; re-read and retry.
                    continue;
                }

                if (diff < 0)
                {
                    // Full: the slot still holds an undrained event one lap behind. Counted as a
                    // rejection, not a loss — the caller decides whether to retry, and retrying is
                    // the normal backpressure response.
                    Interlocked.Increment(ref _rejectedCount);
                    return false;
                }

                // Another producer claimed this position; re-read and retry.
            }
        }

        /// <summary>
        /// Removes the oldest pending event. Owner thread only.
        /// </summary>
        public bool TryDequeue(out T evt)
        {
            Slot[] slots = _slots;
            int mask = _mask;
            int pos = _dequeuePos.Value;
            ref Slot slot = ref slots[pos & mask];
            int sequence = Volatile.Read(ref slot.Sequence);

            if (unchecked(sequence - (pos + 1)) < 0)
            {
                evt = default;
                return false;
            }

            // Copy the payload before releasing the slot back to the producers.
            evt = slot.Data;
            _dequeuePos.Value = unchecked(pos + 1);
            Volatile.Write(ref slot.Sequence, unchecked(pos + mask + 1));

            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                slot.Data = default;
            }

            return true;
        }

        /// <summary>
        /// Drains everything that was pending at entry into the bus on the owner thread.
        /// Events enqueued by handlers during the drain are left for the next drain.
        /// </summary>
        /// <returns>The number of events published.</returns>
        public int FlushTo(EventBus<T> bus)
        {
            return FlushTo(bus, PendingCount);
        }

        /// <summary>
        /// Drains at most <paramref name="maxEvents"/> events into the bus on the owner thread, so a
        /// single flush cannot stall the frame with an unbounded backlog.
        /// </summary>
        /// <returns>The number of events published.</returns>
        public int FlushTo(EventBus<T> bus, int maxEvents)
        {
            if (bus == null)
            {
                throw new ArgumentNullException(nameof(bus));
            }

            if (maxEvents < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxEvents));
            }

            // The budget is captured before the first publish and is a hard ceiling, not a
            // "keep going until empty" loop: a handler that enqueues in response to its own event
            // must feed the next drain. Draining until empty would turn that pattern into an
            // unbounded main-thread stall, which is exactly the frame spike this type exists to
            // prevent.
            int budget = PendingCount;
            if (budget > maxEvents)
            {
                budget = maxEvents;
            }

            int flushed = 0;
            while (flushed < budget && TryDequeue(out T evt))
            {
                bus.Publish(in evt);
                flushed++;
            }

            return flushed;
        }

        /// <summary>
        /// Discards every pending event and adds the discarded total to <see cref="DroppedCount"/>.
        /// Owner thread only; concurrent producers may still be enqueueing, so use this during
        /// teardown rather than as a reset mid-operation.
        ///
        /// These are the only events the queue can call lost: they entered it and were never read.
        /// </summary>
        public void Clear()
        {
            int discarded = 0;
            while (TryDequeue(out _))
            {
                discarded++;
            }

            if (discarded > 0)
            {
                Interlocked.Add(ref _droppedCount, discarded);
            }
        }
    }

    internal static class RuntimeHelpers
    {
        /// <summary>
        /// True when instances of <typeparamref name="T"/> can hold managed references. Used to skip
        /// the tail-clear for pure value payloads, where it would be a pure write of zeros.
        /// </summary>
        public static bool IsReferenceOrContainsReferences<TCheck>()
        {
#if NETCOREAPP2_1_OR_GREATER || NETSTANDARD2_1 || NETSTANDARD2_1_OR_GREATER || UNITY_2021_2_OR_NEWER
            return System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<TCheck>();
#else
            return true;
#endif
        }
    }
}
