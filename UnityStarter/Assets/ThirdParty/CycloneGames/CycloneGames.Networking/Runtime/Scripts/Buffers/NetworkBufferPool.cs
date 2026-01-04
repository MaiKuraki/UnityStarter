using System.Collections.Concurrent;

namespace CycloneGames.Networking.Buffers
{
    /// <summary>
    /// Thread-safe pool for NetworkBuffer instances. Avoids allocation in hot paths.
    /// </summary>
    public static class NetworkBufferPool
    {
        private static readonly ConcurrentBag<NetworkBuffer> _pool = new ConcurrentBag<NetworkBuffer>();
        private const int MaxPoolSize = 32;

        /// <summary>
        /// Get a pooled NetworkBuffer instance. Reset before use. Returns via Dispose().
        /// </summary>
        public static NetworkBuffer Get()
        {
            if (_pool.TryTake(out var buffer))
            {
                buffer.Reset();
                return buffer;
            }
            return new NetworkBuffer();
        }

        /// <summary>
        /// Get a pooled NetworkBuffer initialized with the given data for reading.
        /// </summary>
        public static NetworkBuffer GetWithData(System.ArraySegment<byte> data)
        {
            var buffer = Get();
            buffer.SetBuffer(data);
            return buffer;
        }

        /// <summary>
        /// Return a buffer to the pool. Called automatically by NetworkBuffer.Dispose().
        /// </summary>
        public static void Return(NetworkBuffer buffer)
        {
            if (buffer == null) return;

            buffer.ReturnToPool();

            if (_pool.Count < MaxPoolSize)
            {
                _pool.Add(buffer);
            }
            else
            {
                // Pool is full, release the underlying array
                buffer.ReleaseBuffer();
            }
        }

        /// <summary>
        /// Clear the pool and release all buffers. Call during scene transitions or shutdown.
        /// </summary>
        public static void Clear()
        {
            while (_pool.TryTake(out var buffer))
            {
                buffer.ReleaseBuffer();
            }
        }
    }
}
