using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace CycloneGames.Networking.Security
{
    /// <summary>
    /// Per-connection rate limiter using token bucket algorithm.
    /// Protects against packet flooding and DDoS from malicious clients.
    /// Thread-safe for concurrent access.
    /// </summary>
    public sealed class RateLimiter
    {
        private readonly Dictionary<int, ConnectionBucket> _buckets = new Dictionary<int, ConnectionBucket>(64);
        private readonly object _lock = new object();

        public int MaxMessagesPerSecond { get; set; }
        public long MaxBytesPerSecond { get; set; }
        public int BurstLimit { get; set; }

        public RateLimiter(int maxMessagesPerSecond = 60, long maxBytesPerSecond = 65536, int burstLimit = 20)
        {
            MaxMessagesPerSecond = maxMessagesPerSecond;
            MaxBytesPerSecond = maxBytesPerSecond;
            BurstLimit = burstLimit;
        }

        /// <summary>
        /// Check if a message from this connection should be allowed.
        /// Returns false if the connection has exceeded its rate limit.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryConsume(int connectionId, int payloadBytes, float currentTime)
        {
            ConnectionBucket bucket;
            lock (_lock)
            {
                if (!_buckets.TryGetValue(connectionId, out bucket))
                {
                    bucket = new ConnectionBucket(MaxMessagesPerSecond, MaxBytesPerSecond, BurstLimit, currentTime);
                    _buckets[connectionId] = bucket;
                }
            }

            return bucket.TryConsume(payloadBytes, currentTime);
        }

        /// <summary>
        /// Check without consuming. Useful for pre-validation.
        /// </summary>
        public bool WouldAllow(int connectionId, float currentTime)
        {
            lock (_lock)
            {
                return !_buckets.TryGetValue(connectionId, out var bucket) || bucket.WouldAllow(currentTime);
            }
        }

        public void RemoveConnection(int connectionId)
        {
            lock (_lock) { _buckets.Remove(connectionId); }
        }

        public void Clear()
        {
            lock (_lock) { _buckets.Clear(); }
        }

        /// <summary>
        /// Get current stats for a connection.
        /// </summary>
        public bool GetStats(int connectionId, out int messagesThisSecond, out long bytesThisSecond)
        {
            lock (_lock)
            {
                if (_buckets.TryGetValue(connectionId, out var bucket))
                {
                    messagesThisSecond = bucket.MessagesThisWindow;
                    bytesThisSecond = bucket.BytesThisWindow;
                    return true;
                }
            }
            messagesThisSecond = 0;
            bytesThisSecond = 0;
            return false;
        }

        private sealed class ConnectionBucket
        {
            private readonly int _maxMessages;
            private readonly long _maxBytes;
            private readonly int _burstLimit;
            private int _messageCount;
            private long _byteCount;
            private float _windowStart;
            private int _violations;
            private readonly object _bucketLock = new object();

            public int MessagesThisWindow { get { lock (_bucketLock) return _messageCount; } }
            public long BytesThisWindow { get { lock (_bucketLock) return _byteCount; } }
            public int Violations => _violations;

            public ConnectionBucket(int maxMessages, long maxBytes, int burstLimit, float currentTime)
            {
                _maxMessages = maxMessages;
                _maxBytes = maxBytes;
                _burstLimit = burstLimit;
                _windowStart = currentTime;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool TryConsume(int bytes, float currentTime)
            {
                lock (_bucketLock)
                {
                    RefreshWindow(currentTime);

                    if (_messageCount + 1 > _maxMessages + _burstLimit)
                    {
                        _violations++;
                        return false;
                    }

                    if (_byteCount + bytes > _maxBytes)
                    {
                        _violations++;
                        return false;
                    }

                    _messageCount++;
                    _byteCount += bytes;
                    return true;
                }
            }

            public bool WouldAllow(float currentTime)
            {
                lock (_bucketLock)
                {
                    RefreshWindow(currentTime);
                    return _messageCount < _maxMessages + _burstLimit;
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void RefreshWindow(float currentTime)
            {
                if (currentTime - _windowStart >= 1f)
                {
                    _windowStart = currentTime;
                    _messageCount = 0;
                    _byteCount = 0;
                }
            }
        }
    }
}
