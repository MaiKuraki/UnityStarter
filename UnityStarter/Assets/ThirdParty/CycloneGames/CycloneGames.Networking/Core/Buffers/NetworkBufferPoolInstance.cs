using System;
using System.Collections.Concurrent;
using System.Threading;

namespace CycloneGames.Networking.Buffers
{
    /// <summary>
    /// Instance-scoped owner of pooled <see cref="NetworkBufferStorage"/> instances.
    /// Buffer contents and cursors remain single-owner through a generation-checked
    /// lease, while every mutable counter and every idle buffer belongs to this
    /// instance. Composition roots, sessions and tests can therefore own an
    /// isolated pool instead of sharing a process-wide mutable static.
    ///
    /// The pool is thread-safe for Get/Dispose/Configure; a single lease (and any
    /// span obtained from it) must still be used by one thread at a time.
    /// </summary>
    public sealed class NetworkBufferPoolInstance
    {
        private const int DefaultMaxPoolSize = 32;

        private readonly ConcurrentQueue<NetworkBufferStorage> _pool =
            new ConcurrentQueue<NetworkBufferStorage>();

        private int _count;
        private int _outstandingCount;
        private int _invalidReturnCount;
        private int _maxPoolSize = DefaultMaxPoolSize;
        private int _clearBuffersOnReturn;
        private long _pressureTrimmedBufferCount;

        public int Count => Volatile.Read(ref _count);
        public int OutstandingCount => Volatile.Read(ref _outstandingCount);
        public int InvalidReturnCount => Volatile.Read(ref _invalidReturnCount);
        public int MaxPoolSize => Volatile.Read(ref _maxPoolSize);
        public bool ClearBuffersOnReturn => Volatile.Read(ref _clearBuffersOnReturn) != 0;

        public NetworkBufferPoolMemorySnapshot GetMemorySnapshot()
        {
            return new NetworkBufferPoolMemorySnapshot(
                Count,
                OutstandingCount,
                MaxPoolSize,
                InvalidReturnCount,
                Interlocked.Read(ref _pressureTrimmedBufferCount),
                ClearBuffersOnReturn);
        }

        public void Configure(int maxPoolSize, bool clearBuffersOnReturn = false)
        {
            if (maxPoolSize < 0)
                throw new ArgumentOutOfRangeException(nameof(maxPoolSize));

            Volatile.Write(ref _clearBuffersOnReturn, clearBuffersOnReturn ? 1 : 0);
            Volatile.Write(ref _maxPoolSize, maxPoolSize);
            TrimExcess();
        }

        public NetworkBuffer Get()
        {
            NetworkBufferStorage storage;
            if (_pool.TryDequeue(out storage))
            {
                Interlocked.Decrement(ref _count);
            }
            else
            {
                storage = new NetworkBufferStorage();
            }

            storage.SetClearPolicy(ClearBuffersOnReturn);
            long leaseToken;
            try
            {
                leaseToken = storage.BeginLease();
            }
            catch
            {
                storage.ReleaseBuffer(ClearBuffersOnReturn);
                throw;
            }

            Interlocked.Increment(ref _outstandingCount);
            return new NetworkBuffer(storage, leaseToken, this);
        }

        public NetworkBuffer GetWithData(ArraySegment<byte> data)
        {
            NetworkBuffer buffer = Get();
            try
            {
                buffer.SetBuffer(data);
                return buffer;
            }
            catch
            {
                buffer.Dispose();
                throw;
            }
        }

        public NetworkBuffer GetWithData(ReadOnlySpan<byte> data)
        {
            NetworkBuffer buffer = Get();
            try
            {
                buffer.SetBuffer(data);
                return buffer;
            }
            catch
            {
                buffer.Dispose();
                throw;
            }
        }

        internal void ReturnLease(NetworkBufferStorage storage, long leaseToken)
        {
            if (storage == null || !storage.TryEndLease(leaseToken))
            {
                Interlocked.Increment(ref _invalidReturnCount);
                throw new ObjectDisposedException(
                    nameof(NetworkBuffer),
                    "The network-buffer lease is default, stale, or has already been returned.");
            }

            Interlocked.Decrement(ref _outstandingCount);

            bool clearBuffer = ClearBuffersOnReturn;
            storage.PrepareForPooling(clearBuffer);

            if (!TryReservePoolSlot())
            {
                storage.ReleaseBuffer(clearBuffer: false);
                return;
            }

            _pool.Enqueue(storage);
            // A concurrent Configure may have lowered the limit after the slot was
            // reserved. The returning thread closes that race after publication.
            TrimExcess();
        }

        public void Clear()
        {
            bool clearBuffer = ClearBuffersOnReturn;
            while (_pool.TryDequeue(out NetworkBufferStorage storage))
            {
                Interlocked.Decrement(ref _count);
                storage.ReleaseBuffer(clearBuffer);
            }
        }

        /// <summary>Releases at most <paramref name="maxWork"/> idle buffers and never touches active leases.</summary>
        public NetworkBufferPoolTrimResult TrimIdleStep(int targetIdleCount, int maxWork)
        {
            if (targetIdleCount < 0)
                throw new ArgumentOutOfRangeException(nameof(targetIdleCount));
            if (maxWork < 0)
                throw new ArgumentOutOfRangeException(nameof(maxWork));

            int released = 0;
            bool clearBuffer = ClearBuffersOnReturn;
            while (released < maxWork && Count > targetIdleCount
                   && _pool.TryDequeue(out NetworkBufferStorage storage))
            {
                Interlocked.Decrement(ref _count);
                storage.ReleaseBuffer(clearBuffer);
                released++;
            }

            if (released > 0)
                Interlocked.Add(ref _pressureTrimmedBufferCount, released);

            return new NetworkBufferPoolTrimResult(released, Count);
        }

        private bool TryReservePoolSlot()
        {
            while (true)
            {
                int maxPoolSize = Volatile.Read(ref _maxPoolSize);
                int count = Volatile.Read(ref _count);
                if (count >= maxPoolSize)
                    return false;

                if (Interlocked.CompareExchange(ref _count, count + 1, count) == count)
                    return true;
            }
        }

        private void TrimExcess()
        {
            bool clearBuffer = ClearBuffersOnReturn;
            while (Volatile.Read(ref _count) > Volatile.Read(ref _maxPoolSize)
                   && _pool.TryDequeue(out NetworkBufferStorage storage))
            {
                Interlocked.Decrement(ref _count);
                storage.ReleaseBuffer(clearBuffer);
            }
        }
    }
}
