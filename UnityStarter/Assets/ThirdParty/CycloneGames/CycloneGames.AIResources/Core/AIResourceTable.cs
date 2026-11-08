using System;

namespace CycloneGames.AIResources.Core
{
    /// <summary>Outcome of a claim attempt.</summary>
    public enum AIResourceClaimResult : byte
    {
        /// <summary>The caller now owns the resource.</summary>
        Claimed = 0,

        /// <summary>The caller already owned it; the lease was refreshed.</summary>
        Refreshed = 1,

        /// <summary>Another owner holds it and the attempt did not qualify for preemption.</summary>
        Rejected = 2,

        /// <summary>The caller preempted a lower-priority owner and now holds it.</summary>
        Preempted = 3,

        /// <summary>The table is full.</summary>
        Exhausted = 4
    }

    /// <summary>
    /// One owner per shared resource, with optional priority preemption and lease expiry.
    /// </summary>
    /// <remarks>
    /// The table is engine-free and single-threaded by design: decisions that claim resources already
    /// run on one owner thread, and a lock would serialize them again. Storage is preallocated at
    /// construction, so claim, release and sweep allocate nothing. Expiry exists so a holder that
    /// disappears without releasing cannot keep a resource forever.
    /// </remarks>
    public sealed class AIResourceTable : IDisposable
    {
        private const int Empty = -1;
        private const int Tombstone = -2;

        private readonly int _mask;
        private readonly int[] _buckets;
        private readonly Slot[] _slots;
        private readonly int[] _pendingRemoval;
        private int _slotCount;
        private int _claimedCount;
        private int _tombstoneCount;
        private bool _disposed;

        /// <param name="capacity">Expected simultaneous claims. Rounded up to a power of two.</param>
        public AIResourceTable(int capacity = 64)
        {
            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "Capacity must be positive.");
            }

            int size = 4;
            while (size < capacity * 2)
            {
                size <<= 1;
            }

            _mask = size - 1;
            _buckets = new int[size];
            _slots = new Slot[capacity];
            _pendingRemoval = new int[capacity];

            for (int i = 0; i < _buckets.Length; i++)
            {
                _buckets[i] = Empty;
            }
        }

        /// <summary>Resources currently owned by someone.</summary>
        public int ClaimedCount => _claimedCount;

        /// <summary>Slot capacity, independent of how many resources are claimed.</summary>
        public int Capacity => _slots.Length;

        /// <param name="leaseSeconds">Zero or negative means the claim never expires on its own.</param>
        /// <param name="preempt">Take the resource when the caller's priority is strictly higher.</param>
        public AIResourceClaimResult TryClaim(
            int resourceId,
            int ownerId,
            int priority,
            double nowSeconds,
            double leaseSeconds = 0d,
            bool preempt = false)
        {
            ThrowIfDisposed();

            int bucket = Hash(resourceId) & _mask;
            int reusableBucket = Empty;
            int steps = 0;
            while (steps++ < _buckets.Length)
            {
                int slotIndex = _buckets[bucket];
                if (slotIndex == Tombstone)
                {
                    if (reusableBucket == Empty)
                    {
                        reusableBucket = bucket;
                    }

                    bucket = (bucket + 1) & _mask;
                    continue;
                }

                if (slotIndex == Empty)
                {
                    return Insert(resourceId, ownerId, priority, nowSeconds, leaseSeconds, reusableBucket != Empty ? reusableBucket : bucket);
                }

                ref Slot slot = ref _slots[slotIndex];
                if (slot.ResourceId == resourceId)
                {
                    if (slot.OwnerId == ownerId)
                    {
                        slot.Priority = priority;
                        slot.ExpiryTime = ComputeExpiry(nowSeconds, leaseSeconds);
                        return AIResourceClaimResult.Refreshed;
                    }

                    if (preempt && priority > slot.Priority)
                    {
                        slot.OwnerId = ownerId;
                        slot.Priority = priority;
                        slot.ExpiryTime = ComputeExpiry(nowSeconds, leaseSeconds);
                        return AIResourceClaimResult.Preempted;
                    }

                    return AIResourceClaimResult.Rejected;
                }

                bucket = (bucket + 1) & _mask;
            }

            return AIResourceClaimResult.Exhausted;
        }

        /// <summary>Releases the resource when <paramref name="ownerId"/> holds it.</summary>
        public bool Release(int resourceId, int ownerId)
        {
            ThrowIfDisposed();

            if (!TryFindBucket(resourceId, out int slotIndex, out int bucket))
            {
                return false;
            }

            if (_slots[slotIndex].OwnerId != ownerId)
            {
                return false;
            }

            RemoveSlot(slotIndex, bucket);
            return true;
        }

        /// <summary>Releases every claim held by <paramref name="ownerId"/>. Returns the released count.</summary>
        public int ReleaseOwner(int ownerId)
        {
            ThrowIfDisposed();

            int pending = 0;
            for (int slotIndex = 0; slotIndex < _slotCount && pending < _pendingRemoval.Length; slotIndex++)
            {
                if (_slots[slotIndex].OwnerId == ownerId)
                {
                    _pendingRemoval[pending++] = _slots[slotIndex].ResourceId;
                }
            }

            for (int i = 0; i < pending; i++)
            {
                if (TryFindBucket(_pendingRemoval[i], out int slotIndex, out int bucket))
                {
                    RemoveSlot(slotIndex, bucket);
                }
            }

            return pending;
        }

        public bool IsClaimedBy(int resourceId, int ownerId)
        {
            ThrowIfDisposed();
            return TryFind(resourceId, out Slot slot) && slot.OwnerId == ownerId;
        }

        public bool TryGetOwner(int resourceId, out int ownerId)
        {
            ThrowIfDisposed();
            if (TryFind(resourceId, out Slot slot))
            {
                ownerId = slot.OwnerId;
                return true;
            }

            ownerId = 0;
            return false;
        }

        /// <summary>
        /// Releases claims whose lease expired at <paramref name="nowSeconds"/>. Call it once per AI
        /// update; it is the only place a dead owner is reclaimed.
        /// </summary>
        /// <returns>Number of expired claims.</returns>
        public int SweepExpired(double nowSeconds)
        {
            ThrowIfDisposed();

            int pending = 0;
            for (int slotIndex = 0; slotIndex < _slotCount && pending < _pendingRemoval.Length; slotIndex++)
            {
                double expiry = _slots[slotIndex].ExpiryTime;
                if (expiry > 0d && expiry <= nowSeconds)
                {
                    _pendingRemoval[pending++] = _slots[slotIndex].ResourceId;
                }
            }

            for (int i = 0; i < pending; i++)
            {
                if (TryFindBucket(_pendingRemoval[i], out int slotIndex, out int bucket))
                {
                    RemoveSlot(slotIndex, bucket);
                }
            }

            return pending;
        }

        public void Clear()
        {
            ThrowIfDisposed();

            for (int i = 0; i < _buckets.Length; i++)
            {
                _buckets[i] = Empty;
            }

            Array.Clear(_slots, 0, _slotCount);
            _slotCount = 0;
            _claimedCount = 0;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Clear();
            _disposed = true;
        }

        private static double ComputeExpiry(double nowSeconds, double leaseSeconds)
        {
            return leaseSeconds > 0d && double.IsFinite(leaseSeconds) ? nowSeconds + leaseSeconds : 0d;
        }

        private AIResourceClaimResult Insert(
            int resourceId,
            int ownerId,
            int priority,
            double nowSeconds,
            double leaseSeconds,
            int bucket)
        {
            if (_slotCount >= _slots.Length)
            {
                return AIResourceClaimResult.Exhausted;
            }

            _slots[_slotCount] = new Slot
            {
                Bucket = bucket,
                ResourceId = resourceId,
                OwnerId = ownerId,
                Priority = priority,
                ExpiryTime = ComputeExpiry(nowSeconds, leaseSeconds)
            };
            _buckets[bucket] = _slotCount;
            _slotCount++;
            _claimedCount++;
            return AIResourceClaimResult.Claimed;
        }

        private void RemoveSlot(int slotIndex, int bucket)
        {
            _buckets[bucket] = Tombstone;
            _tombstoneCount++;

            int lastIndex = _slotCount - 1;
            if (slotIndex != lastIndex)
            {
                Slot moved = _slots[lastIndex];
                _slots[slotIndex] = moved;
                _buckets[moved.Bucket] = slotIndex;
            }

            _slots[lastIndex] = default;
            _slotCount--;
            _claimedCount--;

            if (_tombstoneCount > _buckets.Length >> 1)
            {
                RebuildBuckets();
            }
        }

        private void RebuildBuckets()
        {
            for (int i = 0; i < _buckets.Length; i++)
            {
                _buckets[i] = Empty;
            }

            for (int slotIndex = 0; slotIndex < _slotCount; slotIndex++)
            {
                int bucket = Hash(_slots[slotIndex].ResourceId) & _mask;
                while (_buckets[bucket] >= 0)
                {
                    bucket = (bucket + 1) & _mask;
                }

                _slots[slotIndex].Bucket = bucket;
                _buckets[bucket] = slotIndex;
            }

            _tombstoneCount = 0;
        }

        private bool TryFindBucket(int resourceId, out int slotIndex, out int bucket)
        {
            bucket = Hash(resourceId) & _mask;
            int steps = 0;
            while (steps++ < _buckets.Length)
            {
                int candidate = _buckets[bucket];
                if (candidate == Empty)
                {
                    break;
                }

                if (candidate != Tombstone && _slots[candidate].ResourceId == resourceId)
                {
                    slotIndex = candidate;
                    return true;
                }

                bucket = (bucket + 1) & _mask;
            }

            slotIndex = -1;
            return false;
        }

        private bool TryFind(int resourceId, out Slot slot)
        {
            int bucket = Hash(resourceId) & _mask;
            int steps = 0;
            while (steps++ < _buckets.Length)
            {
                int slotIndex = _buckets[bucket];
                if (slotIndex == Empty)
                {
                    slot = default;
                    return false;
                }

                if (slotIndex != Tombstone && _slots[slotIndex].ResourceId == resourceId)
                {
                    slot = _slots[slotIndex];
                    return true;
                }

                bucket = (bucket + 1) & _mask;
            }

            slot = default;
            return false;
        }

        private static int Hash(int resourceId)
        {
            unchecked
            {
                uint x = (uint)resourceId;
                x ^= x >> 16;
                x *= 0x7FEB352Du;
                x ^= x >> 15;
                x *= 0x846CA68Bu;
                x ^= x >> 16;
                return (int)(x & 0x7FFFFFFF);
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(AIResourceTable));
            }
        }

        private struct Slot
        {
            public int Bucket;
            public int ResourceId;
            public int OwnerId;
            public int Priority;

            /// <summary>Zero means the claim never expires.</summary>
            public double ExpiryTime;
        }
    }
}
