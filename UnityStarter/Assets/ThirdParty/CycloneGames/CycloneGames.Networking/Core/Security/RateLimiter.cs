using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace CycloneGames.Networking.Security
{
    /// <summary>
    /// Per-connection rate limiter using token bucket algorithm.
    /// Protects against packet flooding and DDoS from malicious clients.
    /// </summary>
    public sealed class RateLimiter
    {
        private readonly ConcurrentDictionary<int, ConnectionBucket> _buckets = new();

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
            if (payloadBytes < 0) return false;

            ConnectionBucket bucket = _buckets.GetOrAdd(connectionId,
                _ => new ConnectionBucket(MaxMessagesPerSecond, MaxBytesPerSecond, BurstLimit, currentTime));
            return bucket.TryConsume(payloadBytes, currentTime, MaxMessagesPerSecond, MaxBytesPerSecond, BurstLimit);
        }

        /// <summary>
        /// Check without consuming. Useful for pre-validation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool WouldAllow(int connectionId, float currentTime)
        {
            return !_buckets.TryGetValue(connectionId, out var bucket)
                   || bucket.WouldAllow(currentTime, MaxMessagesPerSecond, MaxBytesPerSecond, BurstLimit);
        }

        public void RemoveConnection(int connectionId)
        {
            _buckets.TryRemove(connectionId, out _);
        }

        public void Clear()
        {
            _buckets.Clear();
        }

        /// <summary>
        /// Get current one-second-equivalent consumed budget for a connection.
        /// </summary>
        public bool GetStats(int connectionId, out int messagesThisSecond, out long bytesThisSecond)
        {
            if (_buckets.TryGetValue(connectionId, out var bucket))
            {
                bucket.GetStats(MaxMessagesPerSecond, MaxBytesPerSecond, BurstLimit,
                    out messagesThisSecond, out bytesThisSecond);
                return true;
            }
            messagesThisSecond = 0;
            bytesThisSecond = 0;
            return false;
        }

        /// <summary>
        /// Token bucket per connection. The lock is per connection, so contention is
        /// isolated to the connection currently being checked.
        /// </summary>
        private sealed class ConnectionBucket
        {
            private readonly object _syncRoot = new object();
            private double _messageTokens;
            private double _byteTokens;
            private float _windowStart;
            private int _violations;

            public int Violations => _violations;

            public ConnectionBucket(int maxMessages, long maxBytes, int burstLimit, float currentTime)
            {
                _messageTokens = GetMessageCapacity(maxMessages, burstLimit);
                _byteTokens = GetByteCapacity(maxBytes);
                _windowStart = currentTime;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool TryConsume(int bytes, float currentTime, int maxMessages, long maxBytes, int burstLimit)
            {
                lock (_syncRoot)
                {
                    Refill(currentTime, maxMessages, maxBytes, burstLimit);

                    if (_messageTokens < 1d || _byteTokens < bytes)
                    {
                        _violations++;
                        return false;
                    }

                    _messageTokens -= 1d;
                    _byteTokens -= bytes;
                    return true;
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool WouldAllow(float currentTime, int maxMessages, long maxBytes, int burstLimit)
            {
                lock (_syncRoot)
                {
                    Refill(currentTime, maxMessages, maxBytes, burstLimit);
                    return _messageTokens >= 1d;
                }
            }

            public void GetStats(int maxMessages, long maxBytes, int burstLimit,
                out int messagesThisSecond, out long bytesThisSecond)
            {
                lock (_syncRoot)
                {
                    double messageCapacity = GetMessageCapacity(maxMessages, burstLimit);
                    double byteCapacity = GetByteCapacity(maxBytes);

                    messagesThisSecond = (int)Math.Max(0d, Math.Ceiling(messageCapacity - _messageTokens));
                    bytesThisSecond = (long)Math.Max(0d, Math.Ceiling(byteCapacity - _byteTokens));
                }
            }

            private void Refill(float currentTime, int maxMessages, long maxBytes, int burstLimit)
            {
                float elapsed = currentTime - _windowStart;
                if (elapsed < 0f)
                    elapsed = 0f;

                double messageCapacity = GetMessageCapacity(maxMessages, burstLimit);
                double byteCapacity = GetByteCapacity(maxBytes);

                _messageTokens = Math.Min(messageCapacity, _messageTokens + elapsed * maxMessages);
                _byteTokens = Math.Min(byteCapacity, _byteTokens + elapsed * maxBytes);
                _windowStart = currentTime;
            }

            private static double GetMessageCapacity(int maxMessages, int burstLimit)
            {
                return Math.Max(1, maxMessages) + Math.Max(0, burstLimit);
            }

            private static double GetByteCapacity(long maxBytes)
            {
                return Math.Max(1L, maxBytes);
            }
        }
    }
}
