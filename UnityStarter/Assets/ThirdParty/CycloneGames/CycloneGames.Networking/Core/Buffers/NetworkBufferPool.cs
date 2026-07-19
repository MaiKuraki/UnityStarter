using System;
using System.Collections.Concurrent;
using System.Threading;

namespace CycloneGames.Networking.Buffers
{
    /// <summary>
    /// Thread-safe owner of pooled <see cref="NetworkBufferStorage"/> instances.
    /// Buffer contents and cursors remain single-owner through a generation-checked lease.
    /// </summary>
    public static class NetworkBufferPool
    {
        private const int DefaultMaxPoolSize = 32;

        private static readonly ConcurrentQueue<NetworkBufferStorage> Pool = new ConcurrentQueue<NetworkBufferStorage>();

        private static int _count;
        private static int _outstandingCount;
        private static int _invalidReturnCount;
        private static int _maxPoolSize = DefaultMaxPoolSize;
        private static int _clearBuffersOnReturn;

        public static int Count => Volatile.Read(ref _count);

        /// <summary>Gets the number of active leases that have not been returned.</summary>
        public static int OutstandingCount => Volatile.Read(ref _outstandingCount);

        /// <summary>Gets the number of rejected default, stale, or duplicate returns.</summary>
        public static int InvalidReturnCount => Volatile.Read(ref _invalidReturnCount);

        public static int MaxPoolSize => Volatile.Read(ref _maxPoolSize);

        public static bool ClearBuffersOnReturn => Volatile.Read(ref _clearBuffersOnReturn) != 0;

        public static void Configure(int maxPoolSize, bool clearBuffersOnReturn = false)
        {
            if (maxPoolSize < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxPoolSize));
            }

            Volatile.Write(ref _clearBuffersOnReturn, clearBuffersOnReturn ? 1 : 0);
            Volatile.Write(ref _maxPoolSize, maxPoolSize);
            TrimExcess();
        }

        public static NetworkBuffer Get()
        {
            NetworkBufferStorage storage;
            if (Pool.TryDequeue(out storage))
            {
                Interlocked.Decrement(ref _count);
            }
            else
            {
                storage = new NetworkBufferStorage();
            }

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
            return new NetworkBuffer(storage, leaseToken);
        }

        public static NetworkBuffer GetWithData(ArraySegment<byte> data)
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

        public static NetworkBuffer GetWithData(ReadOnlySpan<byte> data)
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

        internal static void ReturnLease(NetworkBufferStorage storage, long leaseToken)
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

            Pool.Enqueue(storage);
            // A concurrent Configure call may have lowered the limit after the slot was
            // reserved. The returning thread closes that race after publication.
            TrimExcess();
        }

        public static void Clear()
        {
            bool clearBuffer = ClearBuffersOnReturn;
            while (Pool.TryDequeue(out NetworkBufferStorage storage))
            {
                Interlocked.Decrement(ref _count);
                storage.ReleaseBuffer(clearBuffer);
            }
        }

        private static bool TryReservePoolSlot()
        {
            while (true)
            {
                int maxPoolSize = Volatile.Read(ref _maxPoolSize);
                int count = Volatile.Read(ref _count);
                if (count >= maxPoolSize)
                {
                    return false;
                }

                if (Interlocked.CompareExchange(ref _count, count + 1, count) == count)
                {
                    return true;
                }
            }
        }

        private static void TrimExcess()
        {
            bool clearBuffer = ClearBuffersOnReturn;
            while (Volatile.Read(ref _count) > Volatile.Read(ref _maxPoolSize)
                   && Pool.TryDequeue(out NetworkBufferStorage storage))
            {
                Interlocked.Decrement(ref _count);
                storage.ReleaseBuffer(clearBuffer);
            }
        }
    }
}
