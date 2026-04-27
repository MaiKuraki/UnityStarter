using System;
using System.Collections.Generic;

namespace CycloneGames.GameplayAbilities.Networking
{
    public sealed class InMemoryTokenBucketRateLimiter : IConnectionRateLimiter
    {
        private readonly float _capacity;
        private readonly float _refillPerSecond;
        private readonly IGASNetTimeProvider _timeProvider;
        private readonly Dictionary<int, Bucket> _buckets = new Dictionary<int, Bucket>(64);

        private struct Bucket
        {
            public float Tokens;
            public double LastTime;
        }

        public InMemoryTokenBucketRateLimiter(float capacity, float refillPerSecond,
            IGASNetTimeProvider timeProvider = null)
        {
            _capacity = Math.Max(1f, capacity);
            _refillPerSecond = Math.Max(0.01f, refillPerSecond);
            _timeProvider = timeProvider ?? StopwatchGASNetTimeProvider.Instance;
        }

        public bool TryConsume(int connectionId, int tokens)
        {
            if (tokens <= 0) return true;

            double now = _timeProvider.CurrentTimeSeconds;
            if (!_buckets.TryGetValue(connectionId, out var bucket))
            {
                bucket = new Bucket { Tokens = _capacity, LastTime = now };
            }

            double elapsed = now - bucket.LastTime;
            if (elapsed < 0) elapsed = 0;
            float newTokens = bucket.Tokens + (float)(elapsed * _refillPerSecond);
            bucket.Tokens = newTokens < _capacity ? newTokens : _capacity;
            bucket.LastTime = now;

            if (bucket.Tokens < tokens)
            {
                _buckets[connectionId] = bucket;
                return false;
            }

            bucket.Tokens -= tokens;
            _buckets[connectionId] = bucket;
            return true;
        }
    }
}
