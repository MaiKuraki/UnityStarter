using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace CycloneGames.EventBus.Core
{
    /// <summary>
    /// Multi-producer, single-consumer (MPSC) bridge for crossing a thread boundary into a
    /// main-thread-confined <see cref="EventBus{T}"/>: the only supported way to reach a bus from
    /// background work. Any number of threads enqueue; exactly one owner thread drains.
    ///
    /// Bounded Vyukov-style ring with per-slot sequence numbers. Allocation-free on both sides
    /// (no per-event node, unlike ConcurrentQueue). <see cref="TryEnqueue"/> returns false when
    /// full instead of growing, so a stalled consumer becomes observable backpressure.
    ///
    /// Ordering: per-producer order is preserved; there is no global order across producers.
    /// Primitives are limited to Interlocked/Volatile; on single-threaded WebGL the barriers
    /// compile away and the queue degenerates to a plain FIFO.
    /// </summary>
    /// <summary>
    /// Cache-line padding for the ring positions and the contended counters. Size 128 covers
    /// 128-byte lines (ARM64, Apple Silicon); on 64-byte-line targets it only wastes space.
    ///
    /// Declared at namespace scope: a struct nested in a generic type is itself generic, and the
    /// CLR rejects explicit layout on generic types (TypeLoadException on CoreCLR, AOT failure
    /// under IL2CPP).
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 128)]
    internal struct PaddedInt
    {
        [FieldOffset(0)]
        public int Value;
    }

    /// <summary>Cache-line-padded long; see <see cref="PaddedInt"/>.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 128)]
    internal struct PaddedLong
    {
        [FieldOffset(0)]
        public long Value;
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
        private int _closed;
        private long _rejectedAfterCloseCount;

        private PaddedInt _enqueuePos;
        private PaddedInt _dequeuePos;

        // Padded: producers increment _rejectedCount while the consumer writes _dequeuePos and
        // _droppedCount; unpacked they would share the consumer's line and turn every rejection
        // into a cross-core invalidation on the dequeue hot path.
        private PaddedLong _droppedCount;
        private PaddedLong _rejectedCount;

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
        /// Events that entered the queue and were discarded unread by <see cref="Clear"/>.
        /// Thread-safe read. Confirmed loss only; a refused write is <see cref="RejectedCount"/>.
        /// </summary>
        public long DroppedCount => Interlocked.Read(ref _droppedCount.Value);

        /// <summary>
        /// Write attempts refused because the queue was full. Thread-safe read. The
        /// producer-pressure signal; not a loss count.
        /// </summary>
        public long RejectedCount => Interlocked.Read(ref _rejectedCount.Value);

        /// <summary>Approximate pending count. Safe from any thread; treat it as advisory.</summary>
        public int PendingCount
        {
            get
            {
                // Positions stay within Capacity of each other, so the int difference cannot wrap.
                int pending = unchecked(Volatile.Read(ref _enqueuePos.Value) - Volatile.Read(ref _dequeuePos.Value));
                return pending < 0 ? 0 : pending;
            }
        }

        /// <summary>True when no event is pending. Safe from any thread; treat it as advisory.</summary>
        public bool IsEmpty => PendingCount == 0;

        /// <summary>True once <see cref="Close"/> has been called. Producers observe false from
        /// <see cref="TryEnqueue"/> afterwards and must stop, not retry.</summary>
        public bool IsClosed => Volatile.Read(ref _closed) != 0;

        /// <summary>Events rejected because the queue was already closed. Diagnostics only.</summary>
        public long RejectedAfterCloseCount => Interlocked.Read(ref _rejectedAfterCloseCount);

        /// <summary>
        /// Closes the ingress. Idempotent, callable from any thread. Already-queued events remain
        /// drainable: close, let producers observe false, then drain the remainder on the owner
        /// thread. There is no producer join; that belongs to the caller's lifecycle contract.
        /// </summary>
        public void Close()
        {
            Interlocked.Exchange(ref _closed, 1);
        }

        /// <summary>
        /// Enqueues from any thread. Returns false when the queue is full; the event is then the
        /// caller's to retry, drop, or count. Never allocates, never blocks.
        /// </summary>
        public bool TryEnqueue(in T evt)
        {
            // Closed ingress fails permanently; already-queued events stay drainable.
            if (Volatile.Read(ref _closed) != 0)
            {
                Interlocked.Increment(ref _rejectedAfterCloseCount);
                return false;
            }

            Slot[] slots = _slots;
            int mask = _mask;

            while (true)
            {
                int pos = Volatile.Read(ref _enqueuePos.Value);
                ref Slot slot = ref slots[pos & mask];
                int sequence = Volatile.Read(ref slot.Sequence);

                // Relative differences in unchecked int arithmetic: wrap past int.MaxValue is
                // harmless because positions and sequences advance in lockstep.
                int diff = unchecked(sequence - pos);

                if (diff == 0)
                {
                    if (Interlocked.CompareExchange(ref _enqueuePos.Value, pos + 1, pos) == pos)
                    {
                        // Release store publishes Data; the consumer's acquire read observes it.
                        slot.Data = evt;
                        Volatile.Write(ref slot.Sequence, unchecked(pos + 1));
                        return true;
                    }

                    continue;
                }

                if (diff < 0)
                {
                    // Full: the slot holds an undrained event one lap behind.
                    Interlocked.Increment(ref _rejectedCount.Value);
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

            // Hard ceiling captured up front: events enqueued by handlers during the drain feed
            // the next drain, never this one.
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
                Interlocked.Add(ref _droppedCount.Value, discarded);
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
            // NET5_0_OR_GREATER covers modern non-Unity targets (.NET 5+), which the .NET Core era
            // symbols below do not describe. The remaining symbols are kept as they were: Unity's
            // profile does not define every one of them, and the fallback is correct either way.
#if NET5_0_OR_GREATER || NETCOREAPP2_1_OR_GREATER || NETSTANDARD2_1 || NETSTANDARD2_1_OR_GREATER || UNITY_2021_2_OR_NEWER
            return System.Runtime.CompilerServices.RuntimeHelpers.IsReferenceOrContainsReferences<TCheck>();
#else
            return true;
#endif
        }
    }
}
