using System;

namespace CycloneGames.RPGFoundation.Projectile.Networking
{
    /// <summary>
    /// Bounded replay history for rollback and smooth rewind. Each entity owns a
    /// fixed-depth contiguous ring; bucket lookup is an open-addressing index over the
    /// entity id, so record and lookup are O(1) per entity instead of a scan.
    /// All storage is allocated once and every operation is allocation-free.
    /// </summary>
    public sealed class ProjectileNetworkSnapshotHistory
    {
        private const int DEFAULT_DEPTH_PER_ENTITY = 8;

        // Open addressing sentinels for the entity-id index.
        private const int LookupEmpty = -1;
        private const int LookupTombstone = -2;

        private struct Entry
        {
            public ulong EntityId;
            public int Tick;
            public ushort Sequence;
            public ProjectileSnapshotMessage Snapshot;
            public bool IsValid;
        }

        private readonly int _depth;
        private readonly int _bucketCount;
        private readonly Entry[] _entries;
        private readonly ulong[] _bucketEntityIds;
        private readonly int[] _bucketHeads;
        private readonly int[] _bucketCounts;
        private readonly long[] _bucketLastWrite;

        // Free-bucket stack. Scanning for an unoccupied bucket would make first insertion
        // O(buckets).
        private readonly int[] _freeBuckets;
        private int _freeBucketCount;

        // entity id -> bucket, open addressing with linear probing. A linear scan of bucket
        // heads would make filling N entities O(N^2).
        private readonly ulong[] _lookupKeys;
        private readonly int[] _lookupBuckets;
        private readonly int _lookupMask;
        private int _lookupUsed;
        private int _lookupTombstones;

        private long _writeCounter;
        private long _overwriteCount;
        private int _count;
        private ulong _lastEntityId;
        private int _lastBucket;

        public ProjectileNetworkSnapshotHistory(int capacity)
            : this(capacity, DEFAULT_DEPTH_PER_ENTITY)
        {
        }

        public ProjectileNetworkSnapshotHistory(int capacity, int depthPerEntity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            // A depth of one cannot rewind, and it would make the bucket-head array as
            // large as the whole ring.
            if (depthPerEntity < 2)
            {
                throw new ArgumentOutOfRangeException(nameof(depthPerEntity));
            }

            _depth = depthPerEntity;
            _bucketCount = Math.Max(1, (capacity + depthPerEntity - 1) / depthPerEntity);
            _entries = new Entry[_bucketCount * _depth];
            _bucketEntityIds = new ulong[_bucketCount];
            _bucketHeads = new int[_bucketCount];
            _bucketCounts = new int[_bucketCount];
            _bucketLastWrite = new long[_bucketCount];
            _freeBuckets = new int[_bucketCount];
            for (int i = 0; i < _bucketCount; i++)
            {
                _freeBuckets[i] = _bucketCount - 1 - i;
            }

            _freeBucketCount = _bucketCount;

            // Two entries per bucket keeps the load factor at or below 0.5.
            int tableSize = NextPowerOfTwo(_bucketCount * 2);
            _lookupKeys = new ulong[tableSize];
            _lookupBuckets = new int[tableSize];
            _lookupMask = tableSize - 1;
            for (int i = 0; i < tableSize; i++)
            {
                _lookupBuckets[i] = LookupEmpty;
            }

            _lastBucket = -1;
        }

        /// <summary>Total entry slots. A single entity can hold at most DepthPerEntity.</summary>
        public int Capacity => _entries.Length;

        public int Count => _count;

        /// <summary>Lifetime diagnostic; Clear() retains it.</summary>
        public long OverwriteCount => _overwriteCount;

        public int DepthPerEntity => _depth;

        public int BucketCount => _bucketCount;

        public void Record(in ProjectileSnapshotMessage snapshot)
        {
            ValidateEntity(snapshot.ProjectileEntityId);
            int bucket = FindOrCreateBucket(snapshot.ProjectileEntityId);
            int head = _bucketHeads[bucket];
            int index = bucket * _depth + head;

            // A full bucket keeps the same global entry count: the oldest frame
            // is replaced, not removed.
            if (_entries[index].IsValid)
            {
                _overwriteCount++;
            }

            _entries[index].EntityId = snapshot.ProjectileEntityId;
            _entries[index].Tick = snapshot.ServerTick;
            _entries[index].Sequence = snapshot.Sequence;
            _entries[index].Snapshot = snapshot;
            _entries[index].IsValid = true;

            _bucketHeads[bucket] = head + 1 == _depth ? 0 : head + 1;
            if (_bucketCounts[bucket] < _depth)
            {
                _bucketCounts[bucket]++;
                _count++;
            }

            _bucketLastWrite[bucket] = ++_writeCounter;
        }

        public bool TryGet(
            ulong projectileEntityId,
            int serverTick,
            ushort sequence,
            out ProjectileSnapshotMessage snapshot)
        {
            ValidateEntity(projectileEntityId);
            int bucket = FindBucket(projectileEntityId);
            if (bucket >= 0)
            {
                int start = bucket * _depth;
                for (int i = 0; i < _depth; i++)
                {
                    ref readonly Entry entry = ref _entries[start + i];
                    if (entry.IsValid
                        && entry.EntityId == projectileEntityId
                        && entry.Tick == serverTick
                        && entry.Sequence == sequence)
                    {
                        snapshot = entry.Snapshot;
                        return true;
                    }
                }
            }

            snapshot = default;
            return false;
        }

        /// <summary>Newest retained snapshot for an entity, by tick then wrapped sequence.</summary>
        public bool TryGetLatest(ulong projectileEntityId, out ProjectileSnapshotMessage snapshot)
        {
            ValidateEntity(projectileEntityId);
            snapshot = default;
            int bucket = FindBucket(projectileEntityId);
            if (bucket < 0)
            {
                return false;
            }

            bool found = false;
            int bestTick = 0;
            ushort bestSequence = 0;
            int bestIndex = -1;
            int start = bucket * _depth;
            for (int i = 0; i < _depth; i++)
            {
                ref readonly Entry entry = ref _entries[start + i];
                if (!entry.IsValid || entry.EntityId != projectileEntityId)
                {
                    continue;
                }

                if (!found
                    || entry.Tick > bestTick
                    || (entry.Tick == bestTick && IsSequenceNewer(entry.Sequence, bestSequence)))
                {
                    found = true;
                    bestTick = entry.Tick;
                    bestSequence = entry.Sequence;
                    bestIndex = start + i;
                }
            }

            if (!found)
            {
                return false;
            }

            snapshot = _entries[bestIndex].Snapshot;
            return true;
        }

        /// <summary>
        /// Rollback lookup: the newest retained snapshot at or before the requested
        /// server tick. Returns false when the entity has already been rewound past it.
        /// </summary>
        public bool TryGetAtOrBefore(
            ulong projectileEntityId,
            int serverTick,
            out ProjectileSnapshotMessage snapshot)
        {
            ValidateEntity(projectileEntityId);
            snapshot = default;
            int bucket = FindBucket(projectileEntityId);
            if (bucket < 0)
            {
                return false;
            }

            bool found = false;
            int bestTick = 0;
            ushort bestSequence = 0;
            int bestIndex = -1;
            int start = bucket * _depth;
            for (int i = 0; i < _depth; i++)
            {
                ref readonly Entry entry = ref _entries[start + i];
                if (!entry.IsValid || entry.EntityId != projectileEntityId || entry.Tick > serverTick)
                {
                    continue;
                }

                if (!found
                    || entry.Tick > bestTick
                    || (entry.Tick == bestTick && IsSequenceNewer(entry.Sequence, bestSequence)))
                {
                    found = true;
                    bestTick = entry.Tick;
                    bestSequence = entry.Sequence;
                    bestIndex = start + i;
                }
            }

            if (!found)
            {
                return false;
            }

            snapshot = _entries[bestIndex].Snapshot;
            return true;
        }

        /// <summary>One contiguous copy of an entity's retained history, oldest first.</summary>
        public int CopyHistory(ulong projectileEntityId, ProjectileSnapshotMessage[] destination)
        {
            ValidateEntity(projectileEntityId);
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            int bucket = FindBucket(projectileEntityId);
            if (bucket < 0)
            {
                return 0;
            }

            int start = bucket * _depth;
            int valid = _bucketCounts[bucket];
            int head = _bucketHeads[bucket];
            int oldest = valid < _depth ? 0 : head;
            int written = 0;
            for (int i = 0; i < valid && written < destination.Length; i++)
            {
                int index = start + oldest + i;
                if (index >= start + _depth)
                {
                    index -= _depth;
                }

                ref readonly Entry entry = ref _entries[index];
                if (entry.IsValid && entry.EntityId == projectileEntityId)
                {
                    destination[written] = entry.Snapshot;
                    written++;
                }
            }

            return written;
        }

        public int RemoveProjectile(ulong projectileEntityId)
        {
            ValidateEntity(projectileEntityId);
            int bucket = FindBucket(projectileEntityId);
            if (bucket < 0)
            {
                return 0;
            }

            int removed = _bucketCounts[bucket];
            Array.Clear(_entries, bucket * _depth, _depth);
            LookupRemove(projectileEntityId);
            _bucketEntityIds[bucket] = 0UL;
            _bucketHeads[bucket] = 0;
            _bucketCounts[bucket] = 0;
            _bucketLastWrite[bucket] = 0L;
            _freeBuckets[_freeBucketCount] = bucket;
            _freeBucketCount++;
            _count -= removed;
            if (_lastEntityId == projectileEntityId)
            {
                _lastEntityId = 0UL;
                _lastBucket = -1;
            }

            return removed;
        }

        public void Clear()
        {
            Array.Clear(_entries, 0, _entries.Length);
            Array.Clear(_bucketEntityIds, 0, _bucketEntityIds.Length);
            Array.Clear(_bucketHeads, 0, _bucketHeads.Length);
            Array.Clear(_bucketCounts, 0, _bucketCounts.Length);
            Array.Clear(_bucketLastWrite, 0, _bucketLastWrite.Length);
            for (int i = 0; i < _lookupBuckets.Length; i++)
            {
                _lookupBuckets[i] = LookupEmpty;
            }

            _freeBucketCount = _bucketCount;
            for (int i = 0; i < _bucketCount; i++)
            {
                _freeBuckets[i] = _bucketCount - 1 - i;
            }

            _lookupUsed = 0;
            _lookupTombstones = 0;
            _count = 0;
            _writeCounter = 0;
            _lastEntityId = 0UL;
            _lastBucket = -1;
        }

        private int FindBucket(ulong entityId)
        {
            if (_lastBucket >= 0
                && _lastEntityId == entityId
                && _bucketEntityIds[_lastBucket] == entityId)
            {
                return _lastBucket;
            }

            if (TryLookup(entityId, out int bucket))
            {
                _lastEntityId = entityId;
                _lastBucket = bucket;
                return bucket;
            }

            return -1;
        }

        private int FindOrCreateBucket(ulong entityId)
        {
            int existing = FindBucket(entityId);
            if (existing >= 0)
            {
                return existing;
            }

            if (_freeBucketCount > 0)
            {
                _freeBucketCount--;
                int free = _freeBuckets[_freeBucketCount];
                _bucketEntityIds[free] = entityId;
                _bucketHeads[free] = 0;
                _bucketCounts[free] = 0;
                _bucketLastWrite[free] = 0L;
                LookupInsert(entityId, free);
                _lastEntityId = entityId;
                _lastBucket = free;
                return free;
            }

            // All buckets are occupied: evict the least recently written entity. This is a
            // bounded fallback for an under-sized history, not the steady-state path; it
            // stays a full scan because exact LRU is what the diagnostics promise.
            int victim = 0;
            for (int i = 1; i < _bucketCount; i++)
            {
                if (_bucketLastWrite[i] < _bucketLastWrite[victim])
                {
                    victim = i;
                }
            }

            ulong evictedEntityId = _bucketEntityIds[victim];
            int removed = _bucketCounts[victim];
            if (removed > 0)
            {
                _overwriteCount += removed;
                _count -= removed;
            }

            Array.Clear(_entries, victim * _depth, _depth);
            if (evictedEntityId != 0UL)
            {
                LookupRemove(evictedEntityId);
            }

            _bucketEntityIds[victim] = entityId;
            LookupInsert(entityId, victim);
            _bucketHeads[victim] = 0;
            _bucketCounts[victim] = 0;
            _bucketLastWrite[victim] = 0L;
            _lastEntityId = entityId;
            _lastBucket = victim;
            return victim;
        }

        private static bool IsSequenceNewer(ushort candidate, ushort current)
        {
            ushort delta = unchecked((ushort)(candidate - current));
            return delta != 0 && delta < 32768;
        }

        private static void ValidateEntity(ulong entityId)
        {
            if (entityId == 0UL)
            {
                throw new ArgumentOutOfRangeException(nameof(entityId));
            }
        }

        private static int NextPowerOfTwo(int value)
        {
            int result = 1;
            while (result < value)
            {
                result <<= 1;
            }

            return result;
        }

        private static int HashEntityId(ulong key)
        {
            // MurmurHash3 fmix64. Integer-only, so bucket assignment is identical on every
            // platform and never depends on floating point or culture.
            ulong x = key;
            x ^= x >> 33;
            x *= 0xFF51AFD7ED558CCDUL;
            x ^= x >> 33;
            x *= 0xC4CEB9FE1A85EC53UL;
            x ^= x >> 33;
            return (int)(x & 0x7FFFFFFFUL);
        }

        private bool TryLookup(ulong key, out int bucket)
        {
            int index = HashEntityId(key) & _lookupMask;
            for (int probe = 0; probe < _lookupBuckets.Length; probe++)
            {
                int value = _lookupBuckets[index];
                if (value == LookupEmpty)
                {
                    bucket = -1;
                    return false;
                }

                if (value != LookupTombstone && _lookupKeys[index] == key)
                {
                    bucket = value;
                    return true;
                }

                index = (index + 1) & _lookupMask;
            }

            bucket = -1;
            return false;
        }

        private void LookupInsert(ulong key, int bucket)
        {
            if ((_lookupUsed + _lookupTombstones + 1) * 2 > _lookupBuckets.Length)
            {
                LookupRebuild();
            }

            if (!TryLookupInsertRaw(key, bucket))
            {
                LookupRebuild();
                TryLookupInsertRaw(key, bucket);
            }
        }

        private bool TryLookupInsertRaw(ulong key, int bucket)
        {
            int index = HashEntityId(key) & _lookupMask;
            int firstTombstone = -1;
            for (int probe = 0; probe < _lookupBuckets.Length; probe++)
            {
                int value = _lookupBuckets[index];
                if (value == LookupEmpty)
                {
                    if (firstTombstone >= 0)
                    {
                        index = firstTombstone;
                        _lookupTombstones--;
                    }

                    _lookupKeys[index] = key;
                    _lookupBuckets[index] = bucket;
                    _lookupUsed++;
                    return true;
                }

                if (value == LookupTombstone)
                {
                    if (firstTombstone < 0)
                    {
                        firstTombstone = index;
                    }
                }
                else if (_lookupKeys[index] == key)
                {
                    _lookupBuckets[index] = bucket;
                    return true;
                }

                index = (index + 1) & _lookupMask;
            }

            return false;
        }

        private void LookupRemove(ulong key)
        {
            int index = HashEntityId(key) & _lookupMask;
            for (int probe = 0; probe < _lookupBuckets.Length; probe++)
            {
                int value = _lookupBuckets[index];
                if (value == LookupEmpty)
                {
                    return;
                }

                if (value != LookupTombstone && _lookupKeys[index] == key)
                {
                    _lookupBuckets[index] = LookupTombstone;
                    _lookupUsed--;
                    _lookupTombstones++;
                    return;
                }

                index = (index + 1) & _lookupMask;
            }
        }

        private void LookupRebuild()
        {
            for (int i = 0; i < _lookupBuckets.Length; i++)
            {
                _lookupBuckets[i] = LookupEmpty;
            }

            _lookupUsed = 0;
            _lookupTombstones = 0;

            for (int bucket = 0; bucket < _bucketCount; bucket++)
            {
                ulong key = _bucketEntityIds[bucket];
                if (key != 0UL)
                {
                    TryLookupInsertRaw(key, bucket);
                }
            }
        }
    }
}
