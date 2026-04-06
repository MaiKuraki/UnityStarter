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
        private const int MaxPoolSize = 32;
        private static readonly object _returnLock = new object();

        public static NetworkBuffer Get()
        {
            if (_pool.TryDequeue(out var buffer))
            {
                Interlocked.Decrement(ref _count);
                buffer.Reset();
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

        public static void Return(NetworkBuffer buffer)
        {
            if (buffer == null) return;

            buffer.ReturnToPool();

            lock (_returnLock)
            {
                if (_count < MaxPoolSize)
                {
                    _count++;
                    _pool.Enqueue(buffer);
                    return;
                }
            }

            buffer.ReleaseBuffer();
        }

        public static void Clear()
        {
            while (_pool.TryDequeue(out var buffer))
            {
                Interlocked.Decrement(ref _count);
                buffer.ReleaseBuffer();
            }
        }
    }
}
