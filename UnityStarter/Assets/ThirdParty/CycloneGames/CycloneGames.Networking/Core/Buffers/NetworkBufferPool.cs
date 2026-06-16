using System.Collections.Concurrent;
using System.Threading;

namespace CycloneGames.Networking.Buffers
{
    /// <summary>
    /// Thread-safe pool for NetworkBuffer instances. Avoids allocation in hot paths.
    /// </summary>
    public static class NetworkBufferPool
    {
        private static readonly ConcurrentQueue<NetworkBuffer> _pool = new ConcurrentQueue<NetworkBuffer>();
        private static int _count;
        private const int DefaultMaxPoolSize = 32;
        private static int _maxPoolSize = DefaultMaxPoolSize;
        private static volatile bool _clearBuffersOnReturn;
        private static readonly object _returnLock = new object();

        public static int Count => Volatile.Read(ref _count);
        public static int MaxPoolSize => Volatile.Read(ref _maxPoolSize);
        public static bool ClearBuffersOnReturn => _clearBuffersOnReturn;

        public static void Configure(int maxPoolSize = DefaultMaxPoolSize, bool clearBuffersOnReturn = false)
        {
            if (maxPoolSize < 0)
                throw new System.ArgumentOutOfRangeException(nameof(maxPoolSize));

            lock (_returnLock)
            {
                Volatile.Write(ref _maxPoolSize, maxPoolSize);
                _clearBuffersOnReturn = clearBuffersOnReturn;
                TrimExcessLocked();
            }
        }

        public static NetworkBuffer Get()
        {
            if (_pool.TryDequeue(out var buffer))
            {
                Interlocked.Decrement(ref _count);
                buffer.MarkRented();
                return buffer;
            }
            return new NetworkBuffer();
        }

        public static NetworkBuffer GetWithData(System.ArraySegment<byte> data)
        {
            var buffer = Get();
            buffer.SetBuffer(data);
            return buffer;
        }

        public static NetworkBuffer GetWithData(System.ReadOnlySpan<byte> data)
        {
            var buffer = Get();
            buffer.SetBuffer(data);
            return buffer;
        }

        public static void Return(NetworkBuffer buffer)
        {
            if (buffer == null) return;
            if (!buffer.TryMarkReturned()) return;

            bool clearBuffer = _clearBuffersOnReturn;
            buffer.ReturnToPool(clearBuffer);

            lock (_returnLock)
            {
                if (_count < _maxPoolSize)
                {
                    _count++;
                    _pool.Enqueue(buffer);
                    return;
                }
            }

            buffer.ReleaseBuffer(clearBuffer);
        }

        public static void Clear()
        {
            while (_pool.TryDequeue(out var buffer))
            {
                Interlocked.Decrement(ref _count);
                buffer.ReleaseBuffer(_clearBuffersOnReturn);
            }
        }

        public static void ResetConfiguration()
        {
            Configure(DefaultMaxPoolSize, clearBuffersOnReturn: false);
        }

        private static void TrimExcessLocked()
        {
            while (_count > _maxPoolSize && _pool.TryDequeue(out var buffer))
            {
                _count--;
                buffer.ReleaseBuffer(_clearBuffersOnReturn);
            }
        }
    }
}
