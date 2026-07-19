using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace CycloneGames.Networking.Security
{
    /// <summary>
    /// Bounded per-connection token bucket. Configuration is immutable after construction.
    /// Callers must provide a finite monotonic timestamp in seconds.
    /// </summary>
    public sealed class RateLimiter
    {
        private const int DefaultMaxTrackedConnections = 4096;
        private const double DefaultIdleTimeoutSeconds = 120d;

        private readonly ConcurrentDictionary<int, ConnectionBucket> _buckets = new ConcurrentDictionary<int, ConnectionBucket>();
        private readonly object _creationLock = new object();

        public RateLimiter(
            int maxMessagesPerSecond = 60,
            long maxBytesPerSecond = 65536,
            int burstLimit = 20,
            int maxTrackedConnections = DefaultMaxTrackedConnections,
            double idleTimeoutSeconds = DefaultIdleTimeoutSeconds)
        {
            if (maxMessagesPerSecond <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxMessagesPerSecond));
            if (maxBytesPerSecond <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxBytesPerSecond));
            if (burstLimit < 0)
                throw new ArgumentOutOfRangeException(nameof(burstLimit));
            if (maxTrackedConnections <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxTrackedConnections));
            if (!IsFiniteNonNegative(idleTimeoutSeconds) || idleTimeoutSeconds == 0d)
                throw new ArgumentOutOfRangeException(nameof(idleTimeoutSeconds));

            MaxMessagesPerSecond = maxMessagesPerSecond;
            MaxBytesPerSecond = maxBytesPerSecond;
            BurstLimit = burstLimit;
            MaxTrackedConnections = maxTrackedConnections;
            IdleTimeoutSeconds = idleTimeoutSeconds;
        }

        public int MaxMessagesPerSecond { get; }
        public long MaxBytesPerSecond { get; }
        public int BurstLimit { get; }
        public int MaxTrackedConnections { get; }
        public double IdleTimeoutSeconds { get; }
        public int TrackedConnectionCount => _buckets.Count;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryConsume(int connectionId, int payloadBytes, double currentTime)
        {
            if (connectionId <= 0 || payloadBytes < 0 || !IsFiniteNonNegative(currentTime))
                return false;

            ConnectionBucket bucket = GetOrCreateBucket(connectionId, currentTime);
            return bucket != null && bucket.TryConsume(payloadBytes, currentTime);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool WouldAllow(int connectionId, double currentTime)
        {
            if (connectionId <= 0 || !IsFiniteNonNegative(currentTime))
                return false;

            if (_buckets.TryGetValue(connectionId, out ConnectionBucket bucket))
                return bucket.WouldAllow(currentTime);

            return _buckets.Count < MaxTrackedConnections;
        }

        public void RemoveConnection(int connectionId)
        {
            if (connectionId <= 0
                || !_buckets.TryGetValue(connectionId, out ConnectionBucket bucket))
            {
                return;
            }

            bucket.Retire();
            ((ICollection<KeyValuePair<int, ConnectionBucket>>)_buckets).Remove(
                new KeyValuePair<int, ConnectionBucket>(connectionId, bucket));
        }

        public int PruneExpired(double currentTime, int maxRemovals = int.MaxValue)
        {
            if (!IsFiniteNonNegative(currentTime))
                throw new ArgumentOutOfRangeException(nameof(currentTime));
            if (maxRemovals < 0)
                throw new ArgumentOutOfRangeException(nameof(maxRemovals));

            int removed = 0;
            foreach (var pair in _buckets)
            {
                if (removed >= maxRemovals)
                    break;

                if (pair.Value.TryRetireIfExpired(currentTime, IdleTimeoutSeconds)
                    && ((ICollection<KeyValuePair<int, ConnectionBucket>>)_buckets).Remove(pair))
                {
                    removed++;
                }
            }

            return removed;
        }

        public void Clear()
        {
            lock (_creationLock)
            {
                foreach (var pair in _buckets)
                {
                    pair.Value.Retire();
                    ((ICollection<KeyValuePair<int, ConnectionBucket>>)_buckets).Remove(pair);
                }
            }
        }

        public bool GetStats(int connectionId, out int consumedMessages, out long consumedBytes)
        {
            if (_buckets.TryGetValue(connectionId, out ConnectionBucket bucket))
            {
                bucket.GetStats(out consumedMessages, out consumedBytes);
                return true;
            }

            consumedMessages = 0;
            consumedBytes = 0;
            return false;
        }

        private ConnectionBucket GetOrCreateBucket(int connectionId, double currentTime)
        {
            if (_buckets.TryGetValue(connectionId, out ConnectionBucket existing))
                return existing;

            lock (_creationLock)
            {
                if (_buckets.TryGetValue(connectionId, out existing))
                    return existing;

                if (_buckets.Count >= MaxTrackedConnections)
                    PruneExpired(currentTime, Math.Max(1, MaxTrackedConnections / 16));
                if (_buckets.Count >= MaxTrackedConnections)
                    return null;

                var created = new ConnectionBucket(this, currentTime);
                return _buckets.TryAdd(connectionId, created)
                    ? created
                    : (_buckets.TryGetValue(connectionId, out existing) ? existing : null);
            }
        }

        private static bool IsFiniteNonNegative(double value)
        {
            return value >= 0d && !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private sealed class ConnectionBucket
        {
            private readonly object _syncRoot = new object();
            private readonly double _messageCapacity;
            private readonly double _byteCapacity;
            private readonly double _messagesPerSecond;
            private readonly double _bytesPerSecond;

            private double _messageTokens;
            private double _byteTokens;
            private double _lastTimestamp;
            private bool _retired;
            public ConnectionBucket(RateLimiter owner, double currentTime)
            {
                _messageCapacity = (double)owner.MaxMessagesPerSecond + owner.BurstLimit;
                _byteCapacity = owner.MaxBytesPerSecond;
                _messagesPerSecond = owner.MaxMessagesPerSecond;
                _bytesPerSecond = owner.MaxBytesPerSecond;
                _messageTokens = _messageCapacity;
                _byteTokens = _byteCapacity;
                _lastTimestamp = currentTime;
            }

            public bool TryConsume(int bytes, double currentTime)
            {
                lock (_syncRoot)
                {
                    if (_retired)
                        return false;

                    if (!TryRefill(currentTime))
                        return false;

                    if (_messageTokens < 1d || _byteTokens < bytes)
                        return false;

                    _messageTokens -= 1d;
                    _byteTokens -= bytes;
                    return true;
                }
            }

            public bool WouldAllow(double currentTime)
            {
                lock (_syncRoot)
                {
                    if (_retired)
                        return false;

                    if (!TryRefill(currentTime))
                        return false;

                    return _messageTokens >= 1d;
                }
            }

            public bool TryRetireIfExpired(double currentTime, double idleTimeoutSeconds)
            {
                lock (_syncRoot)
                {
                    if (_retired || currentTime - _lastTimestamp < idleTimeoutSeconds)
                        return false;

                    _retired = true;
                    return true;
                }
            }

            public void Retire()
            {
                lock (_syncRoot)
                    _retired = true;
            }

            public void GetStats(out int consumedMessages, out long consumedBytes)
            {
                lock (_syncRoot)
                {
                    consumedMessages = (int)Math.Max(0d, Math.Ceiling(_messageCapacity - _messageTokens));
                    consumedBytes = (long)Math.Max(0d, Math.Ceiling(_byteCapacity - _byteTokens));
                }
            }

            private bool TryRefill(double currentTime)
            {
                double elapsed = currentTime - _lastTimestamp;
                if (elapsed < 0d)
                    return false;

                _messageTokens = Math.Min(_messageCapacity, _messageTokens + elapsed * _messagesPerSecond);
                _byteTokens = Math.Min(_byteCapacity, _byteTokens + elapsed * _bytesPerSecond);
                _lastTimestamp = currentTime;
                return true;
            }
        }
    }
}
