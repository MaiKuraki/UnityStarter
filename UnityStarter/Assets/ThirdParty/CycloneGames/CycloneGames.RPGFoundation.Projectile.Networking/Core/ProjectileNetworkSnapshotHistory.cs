using System;

namespace CycloneGames.RPGFoundation.Projectile.Networking
{
    /// <summary>
    /// Bounded replay history for rollback and smooth rewind. Each entity owns a
    /// fixed-depth contiguous ring; bucket heads are a compact sequential array.
    /// All storage is allocated once, every operation is allocation-free, and a miss
    /// scans bucket heads (a few KB of sequential memory) instead of the entire ring.
    /// </summary>
    public sealed class ProjectileNetworkSnapshotHistory
    {
        private const int DEFAULT_DEPTH_PER_ENTITY = 8;

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

            // A depth of one cannot rewind and makes the bucket-head array as large
            // as the whole ring, degrading a miss back to a full linear scan.
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
                    Entry entry = _entries[start + i];
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
                Entry entry = _entries[start + i];
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
                Entry entry = _entries[start + i];
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

                Entry entry = _entries[index];
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
            _bucketEntityIds[bucket] = 0UL;
            _bucketHeads[bucket] = 0;
            _bucketCounts[bucket] = 0;
            _bucketLastWrite[bucket] = 0L;
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
            _count = 0;
            _writeCounter = 0;
            _lastEntityId = 0UL;
            _lastBucket = -1;
        }

        /// <summary>
        /// Bucket heads are scanned sequentially: they occupy a compact array that stays
        /// resident in cache, and no hash probe, tombstone or deletion rehash is needed.
        /// </summary>
        private int FindBucket(ulong entityId)
        {
            if (_lastBucket >= 0
                && _lastEntityId == entityId
                && _bucketEntityIds[_lastBucket] == entityId)
            {
                return _lastBucket;
            }

            for (int i = 0; i < _bucketCount; i++)
            {
                if (_bucketEntityIds[i] == entityId)
                {
                    _lastEntityId = entityId;
                    _lastBucket = i;
                    return i;
                }
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

            for (int i = 0; i < _bucketCount; i++)
            {
                if (_bucketEntityIds[i] == 0UL)
                {
                    _bucketEntityIds[i] = entityId;
                    _lastEntityId = entityId;
                    _lastBucket = i;
                    return i;
                }
            }

            // All buckets are occupied: evict the least recently written entity.
            int victim = 0;
            for (int i = 1; i < _bucketCount; i++)
            {
                if (_bucketLastWrite[i] < _bucketLastWrite[victim])
                {
                    victim = i;
                }
            }

            int removed = _bucketCounts[victim];
            if (removed > 0)
            {
                _overwriteCount += removed;
                _count -= removed;
            }

            Array.Clear(_entries, victim * _depth, _depth);
            _bucketEntityIds[victim] = entityId;
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
    }
}
