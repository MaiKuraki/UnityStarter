using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;

namespace CycloneGames.Networking.Security
{
    /// <summary>
    /// Per-connection rate limiter using token bucket algorithm.
    /// Protects against packet flooding and DDoS from malicious clients.
    /// Lock-free on the consume hot path after bucket creation.
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
        /// Lock-free on the hot path; bucket creation uses ConcurrentDictionary's
        /// built-in GetOrAdd which is lock-free for reads.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryConsume(int connectionId, int payloadBytes, float currentTime)
        {
            ConnectionBucket bucket = _buckets.GetOrAdd(connectionId,
                _ => new ConnectionBucket(MaxMessagesPerSecond, MaxBytesPerSecond, BurstLimit, currentTime));
            return bucket.TryConsume(payloadBytes, currentTime);
        }

        /// <summary>
        /// Check without consuming. Useful for pre-validation.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool WouldAllow(int connectionId, float currentTime)
        {
            return !_buckets.TryGetValue(connectionId, out var bucket) || bucket.WouldAllow(currentTime);
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
        /// Get current stats for a connection.
        /// </summary>
        public bool GetStats(int connectionId, out int messagesThisSecond, out long bytesThisSecond)
        {
            if (_buckets.TryGetValue(connectionId, out var bucket))
            {
                messagesThisSecond = bucket.MessagesThisWindow;
                bytesThisSecond = bucket.BytesThisWindow;
                return true;
            }
            messagesThisSecond = 0;
            bytesThisSecond = 0;
            return false;
        }

        /// <summary>
        /// Lock-free token bucket per connection.
        /// All state mutations use <see cref="Interlocked"/> for thread safety
        /// without lock acquisition on the consume hot path.
        /// </summary>
        private sealed class ConnectionBucket
        {
            private readonly int _maxMessages;
            private readonly long _maxBytes;
            private readonly int _burstLimit;

            private int _messageCount;
            private long _byteCount;
            private float _windowStart;
            private int _violations;

            public int MessagesThisWindow => Volatile.Read(ref _messageCount);
            public long BytesThisWindow => Interlocked.Read(ref _byteCount);
            public int Violations => Volatile.Read(ref _violations);

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
                RefreshWindow(currentTime);

                int msgCount = Interlocked.Increment(ref _messageCount);
                if (msgCount > _maxMessages + _burstLimit)
                {
                    Interlocked.Decrement(ref _messageCount);
                    Interlocked.Increment(ref _violations);
                    return false;
                }

                long byteTotal = Interlocked.Add(ref _byteCount, bytes);
                if (byteTotal > _maxBytes)
                {
                    Interlocked.Add(ref _byteCount, -bytes);
                    Interlocked.Decrement(ref _messageCount);
                    Interlocked.Increment(ref _violations);
                    return false;
                }

                return true;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool WouldAllow(float currentTime)
            {
                RefreshWindow(currentTime);
                return Volatile.Read(ref _messageCount) < _maxMessages + _burstLimit;
            }

            /// <summary>
            /// Reset counters when the 1-second window elapses.
            /// Handles clock corrections (NTP time jumps backwards) by resetting
            /// the window start to current time rather than letting the window drift.
            /// </summary>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private void RefreshWindow(float currentTime)
            {
                float windowStart = _windowStart;
                float elapsed = currentTime - windowStart;

                if (elapsed >= 1f || elapsed < 0f)
                {
                    // Only one thread should perform the reset. CompareExchange
                    // ensures atomicity: if another thread raced ahead and already
                    // updated _windowStart, the CAS fails and we skip the reset.
                    float expected = windowStart;
                    if (Interlocked.CompareExchange(ref _windowStart, currentTime, expected) == expected)
                    {
                        Interlocked.Exchange(ref _messageCount, 0);
                        Interlocked.Exchange(ref _byteCount, 0);
                    }
                }
            }
        }
    }
}
