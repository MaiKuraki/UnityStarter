using System;

namespace CycloneGames.Spatial.Core.Internal
{
    /// <summary>
    /// Open-addressing cell bucket table with linear probing, tombstones and in-place rebuild.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One bucket maps a <see cref="CellCoordinate"/> to the head slot of that cell's chain. Flat key, head and state
    /// arrays; no per-entry object.
    /// </para>
    /// <para>
    /// Deletion uses tombstones, because clearing an occupied bucket would cut the probe chain and orphan every entry
    /// behind it. Tombstones are reclaimed by rebuilding once they exceed a quarter of the capacity.
    /// </para>
    /// <para>Load factor is held at or below 70 percent, which bounds probe length and guarantees termination.</para>
    /// </remarks>
    internal sealed class BucketTable
    {
        private const byte StateEmpty = 0;
        private const byte StateOccupied = 1;
        private const byte StateTombstone = 2;

        private const int MinimumCapacity = 16;
        private const int MaximumCapacity = 1 << 26;

        private CellCoordinate[] _keys;
        private int[] _heads;
        private byte[] _states;
        private int _capacity;
        private int _mask;
        private int _occupied;
        private int _tombstones;

        public BucketTable(int initialCapacity)
        {
            _capacity = NextPowerOfTwo(initialCapacity < MinimumCapacity ? MinimumCapacity : initialCapacity);
            _mask = _capacity - 1;
            _keys = new CellCoordinate[_capacity];
            _heads = new int[_capacity];
            _states = new byte[_capacity];
        }

        /// <summary>Buckets that currently hold at least one element.</summary>
        public int OccupiedCount
        {
            get { return _occupied; }
        }

        /// <summary>Total buckets, including empty and tombstone buckets.</summary>
        public int Capacity
        {
            get { return _capacity; }
        }

        /// <summary>Length of the longest probe sequence observed since the last rebuild. Diagnostic only.</summary>
        public int PeakProbeLength { get; private set; }

        /// <summary>
        /// Number of table rebuilds since construction. Each rebuild is O(capacity) and rehashes every occupied bucket,
        /// so a rising count during steady state means the bucket capacity was under-reserved.
        /// </summary>
        public int RebuildCount { get; private set; }

        public int HeadAt(int bucket)
        {
            return _heads[bucket];
        }

        /// <summary>True when the bucket currently maps a key to a chain head.</summary>
        public bool IsOccupied(int bucket)
        {
            return _states[bucket] == StateOccupied;
        }

        /// <summary>Key stored in an occupied bucket. Only valid when <see cref="IsOccupied"/> is true.</summary>
        public CellCoordinate KeyAt(int bucket)
        {
            return _keys[bucket];
        }

        public void SetHeadAt(int bucket, int head)
        {
            _heads[bucket] = head;
        }

        /// <summary>
        /// Grows the table so it can address <paramref name="bucketCount"/> distinct cells at the target load factor.
        /// </summary>
        /// <remarks>Complexity: O(new capacity). Called from the index's explicit reservation path.</remarks>
        public void EnsureBucketCount(int bucketCount)
        {
            while ((long)_capacity * 7 / 10 < bucketCount)
            {
                if (_capacity >= MaximumCapacity)
                {
                    throw new InvalidOperationException("Bucket table capacity limit reached.");
                }

                Rebuild(_capacity * 2);
            }
        }

        public void Clear()
        {
            Array.Clear(_states, 0, _capacity);
            _occupied = 0;
            _tombstones = 0;
            PeakProbeLength = 0;
        }

        /// <summary>
        /// Returns the bucket index for <paramref name="key"/>, or -1 when the key is absent.
        /// </summary>
        /// <remarks>Complexity: O(1) expected, O(capacity) worst case.</remarks>
        public int Find(in CellCoordinate key)
        {
            int index = (int)(key.ComputeHash() & (uint)_mask);
            for (int step = 0; step < _capacity; step++)
            {
                byte state = _states[index];
                if (state == StateEmpty)
                {
                    return -1;
                }

                if (state == StateOccupied && _keys[index] == key)
                {
                    return index;
                }

                index = (index + 1) & _mask;
            }

            return -1;
        }

        /// <summary>
        /// Returns the bucket index for <paramref name="key"/>, creating an empty bucket with head -1 when absent.
        /// </summary>
        /// <remarks>
        /// Complexity: amortized O(1); the table may rebuild before the probe, which is O(capacity).
        /// The rebuild happens before the probe so the returned index is never invalidated by it.
        /// </remarks>
        public int FindOrInsert(in CellCoordinate key)
        {
            EnsureRoomForOneMore();

            int index = (int)(key.ComputeHash() & (uint)_mask);
            int tombstoneIndex = -1;
            int probeLength = 0;

            for (int step = 0; step < _capacity; step++)
            {
                probeLength++;
                byte state = _states[index];
                if (state == StateEmpty)
                {
                    int target = tombstoneIndex >= 0 ? tombstoneIndex : index;
                    if (tombstoneIndex >= 0)
                    {
                        _tombstones--;
                    }

                    _states[target] = StateOccupied;
                    _keys[target] = key;
                    _heads[target] = -1;
                    _occupied++;

                    if (probeLength > PeakProbeLength)
                    {
                        PeakProbeLength = probeLength;
                    }

                    return target;
                }

                if (state == StateOccupied)
                {
                    if (_keys[index] == key)
                    {
                        if (probeLength > PeakProbeLength)
                        {
                            PeakProbeLength = probeLength;
                        }

                        return index;
                    }
                }
                else if (tombstoneIndex < 0)
                {
                    tombstoneIndex = index;
                }

                index = (index + 1) & _mask;
            }

            // Unreachable while the load factor is capped, kept as a hard stop so a corrupted table cannot hang.
            throw new InvalidOperationException("Bucket table probe exceeded capacity.");
        }

        /// <summary>Converts an occupied bucket into a tombstone.</summary>
        public void Remove(int bucket)
        {
            _states[bucket] = StateTombstone;
            _heads[bucket] = -1;
            _keys[bucket] = default;
            _occupied--;
            _tombstones++;
        }

        private void EnsureRoomForOneMore()
        {
            while ((_occupied + _tombstones + 1) * 10 > _capacity * 7)
            {
                if ((_occupied + 1) * 10 > _capacity * 7)
                {
                    if (_capacity >= MaximumCapacity)
                    {
                        throw new InvalidOperationException("Bucket table capacity limit reached.");
                    }

                    Rebuild(_capacity * 2);
                }
                else
                {
                    Rebuild(_capacity);
                }
            }
        }

        private void Rebuild(int newCapacity)
        {
            CellCoordinate[] oldKeys = _keys;
            int[] oldHeads = _heads;
            byte[] oldStates = _states;
            int oldCapacity = _capacity;

            _capacity = newCapacity;
            _mask = _capacity - 1;
            _keys = new CellCoordinate[_capacity];
            _heads = new int[_capacity];
            _states = new byte[_capacity];
            _occupied = 0;
            _tombstones = 0;
            PeakProbeLength = 0;
            RebuildCount++;

            for (int i = 0; i < oldCapacity; i++)
            {
                if (oldStates[i] != StateOccupied)
                {
                    continue;
                }

                CellCoordinate key = oldKeys[i];
                int index = (int)(key.ComputeHash() & (uint)_mask);
                while (_states[index] == StateOccupied)
                {
                    index = (index + 1) & _mask;
                }

                _states[index] = StateOccupied;
                _keys[index] = key;
                _heads[index] = oldHeads[i];
                _occupied++;
            }
        }

        private static int NextPowerOfTwo(int value)
        {
            int result = MinimumCapacity;
            while (result < value)
            {
                if (result >= MaximumCapacity)
                {
                    return MaximumCapacity;
                }

                result <<= 1;
            }

            return result;
        }
    }
}
