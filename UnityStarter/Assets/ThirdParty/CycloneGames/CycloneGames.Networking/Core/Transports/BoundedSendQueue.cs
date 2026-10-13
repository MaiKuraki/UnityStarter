using System;
using System.Buffers;

namespace CycloneGames.Networking.Transports
{
    public enum SendQueueOverflowPolicy : byte
    {
        /// <summary>New item is rejected; the queue keeps its current content. Default, fail-safe.</summary>
        RejectNew = 0,
        /// <summary>The oldest queued item is evicted to admit the new one. Best for state snapshots where staleness is worse than loss.</summary>
        DropOldest = 1
    }

    public enum SendQueueEnqueueResult : byte
    {
        Enqueued = 0,
        Full = 1,
        Evicted = 2
    }

    /// <summary>
    /// Bounded FIFO send queue with a deterministic overflow policy, for absorbing
    /// short write bursts (pulse traffic) without unbounded memory growth. Payload
    /// bytes are copied into a shared pool-owned slab on enqueue and returned on
    /// dequeue, so queued payloads do not pin caller buffers.
    /// Single owner thread; not concurrent.
    /// </summary>
    public sealed class BoundedSendQueue : IDisposable
    {
        private struct Entry
        {
            public byte[] Buffer;
            public int Length;
            public int ChannelId;
            public bool OnLoan;
        }

        private readonly Entry[] _entries;
        private readonly SendQueueOverflowPolicy _policy;
        private readonly int _maxPayloadBytes;
        private int _head;
        private int _count;
        private long _droppedCount;
        private long _enqueuedCount;
        private bool _disposed;

        public BoundedSendQueue(int capacity, SendQueueOverflowPolicy policy = SendQueueOverflowPolicy.RejectNew, int maxPayloadBytes = NetworkConstants.MaxMTU)
        {
            if (capacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(capacity));
            if (maxPayloadBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxPayloadBytes));

            _entries = new Entry[capacity];
            _policy = policy;
            _maxPayloadBytes = maxPayloadBytes;
        }

        public int Capacity => _entries.Length;
        public int Count => _count;
        public long DroppedCount => _droppedCount;
        public long EnqueuedCount => _enqueuedCount;

        public SendQueueEnqueueResult Enqueue(in ArraySegment<byte> payload, int channelId)
        {
            ThrowIfDisposed();
            if (payload.Array == null || payload.Count == 0 || payload.Count > _maxPayloadBytes)
                throw new ArgumentOutOfRangeException(nameof(payload));

            bool evicted = false;
            if (_count == _entries.Length)
            {
                if (_policy == SendQueueOverflowPolicy.RejectNew)
                    return SendQueueEnqueueResult.Full;

                ReturnEntryBuffer(ref _entries[_head]);
                _head = (_head + 1) % _entries.Length;
                _count--;
                _droppedCount++;
                evicted = true;
            }

            int tail = (_head + _count) % _entries.Length;
            ReturnEntryBuffer(ref _entries[tail]);
            byte[] buffer = ArrayPool<byte>.Shared.Rent(payload.Count);
            Buffer.BlockCopy(payload.Array, payload.Offset, buffer, 0, payload.Count);
            _entries[tail] = new Entry { Buffer = buffer, Length = payload.Count, ChannelId = channelId, OnLoan = false };
            _count++;
            _enqueuedCount++;
            return evicted ? SendQueueEnqueueResult.Evicted : SendQueueEnqueueResult.Enqueued;
        }

        /// <summary>
        /// Ring-lease dequeue: the segment stays valid until the ring wraps and this slot
        /// is reused by a later <see cref="Enqueue"/>, i.e. for at least Capacity-1 further
        /// dequeues. Callers must send or copy it within that window.
        /// </summary>
        public bool TryDequeue(out ArraySegment<byte> payload, out int channelId)
        {
            ThrowIfDisposed();
            if (_count == 0)
            {
                payload = default;
                channelId = 0;
                return false;
            }

            ref Entry entry = ref _entries[_head];
            entry.OnLoan = true;
            payload = new ArraySegment<byte>(entry.Buffer, 0, entry.Length);
            channelId = entry.ChannelId;
            _head = (_head + 1) % _entries.Length;
            _count--;
            return true;
        }

        /// <summary>Invalidates every outstanding dequeued segment. Call only when no loaned payload is in flight.</summary>
        public void Clear()
        {
            ThrowIfDisposed();
            for (int i = 0; i < _entries.Length; i++)
                ReturnEntryBuffer(ref _entries[i]);
            _head = 0;
            _count = 0;
        }

        private static void ReturnEntryBuffer(ref Entry entry)
        {
            if (entry.Buffer != null)
            {
                ArrayPool<byte>.Shared.Return(entry.Buffer);
                entry.Buffer = null;
            }
            entry.Length = 0;
            entry.OnLoan = false;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(BoundedSendQueue));
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            for (int i = 0; i < _entries.Length; i++)
                ReturnEntryBuffer(ref _entries[i]);
        }
    }
}
