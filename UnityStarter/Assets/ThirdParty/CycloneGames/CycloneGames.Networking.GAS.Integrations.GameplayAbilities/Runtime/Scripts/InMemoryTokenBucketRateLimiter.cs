using System;
using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.Networking.GAS.Integrations.GameplayAbilities
{
    /// <summary>
    /// In-memory token bucket limiter keyed by connection ID.
    /// </summary>
    public sealed class InMemoryTokenBucketRateLimiter : IConnectionRateLimiter
    {
        private readonly float _capacity;
        private readonly float _refillPerSecond;
        private readonly Dictionary<int, Bucket> _buckets = new Dictionary<int, Bucket>(64);

        private struct Bucket
        {
            public float Tokens;
            public float LastTime;
        }

        public InMemoryTokenBucketRateLimiter(float capacity, float refillPerSecond)
        {
            _capacity = Mathf.Max(1f, capacity);
            _refillPerSecond = Mathf.Max(0.01f, refillPerSecond);
        }

        public bool TryConsume(int connectionId, int tokens)
        {
            if (tokens <= 0) return true;

            float now = Time.unscaledTime;
            if (!_buckets.TryGetValue(connectionId, out var bucket))
            {
                bucket = new Bucket
                {
                    Tokens = _capacity,
                    LastTime = now
                };
            }

            float elapsed = Math.Max(0f, now - bucket.LastTime);
            bucket.Tokens = Math.Min(_capacity, bucket.Tokens + elapsed * _refillPerSecond);
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