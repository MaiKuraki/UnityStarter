using System;
using System.Runtime.CompilerServices;

namespace CycloneGames.Factory.Runtime
{
    /// <summary>
    /// Reference-identity map from an owned item to its active-lease index, or <c>-1</c> when the item is
    /// owned but inactive. Chained open addressing over one contiguous entry array with an index free list,
    /// so a lookup is a single bucket probe and steady-state operations allocate nothing.
    /// </summary>
    internal sealed class ReferenceOwnershipMap<TKey> where TKey : class
    {
        private const int MinimumCapacity = 4;

        private struct Entry
        {
            public int HashCode;
            public int Next;
            public TKey Key;
            public int Value;
        }

        private int[] _buckets;
        private Entry[] _entries;
        private int _count;
        private int _freeList;
        private int _freeCount;

        public ReferenceOwnershipMap(int capacity)
        {
            int size = RoundUpToPowerOfTwo(capacity < MinimumCapacity ? MinimumCapacity : capacity);
            _buckets = new int[size];
            _entries = new Entry[size];
            _freeList = -1;
        }

        /// <summary>Live entry count.</summary>
        public int Count => _count - _freeCount;

        /// <summary>Entry slots currently reserved. No growth allocation happens below this count.</summary>
        public int Capacity => _entries.Length;

        public bool TryAdd(TKey key, int value)
        {
            int hash = HashOf(key);
            ref int bucket = ref _buckets[hash & (_buckets.Length - 1)];

            for (int index = bucket - 1; index >= 0; index = _entries[index].Next)
            {
                if (_entries[index].HashCode == hash && ReferenceEquals(_entries[index].Key, key))
                {
                    return false;
                }
            }

            int entryIndex;
            if (_freeCount > 0)
            {
                entryIndex = _freeList;
                _freeList = _entries[entryIndex].Next;
                _freeCount--;
            }
            else
            {
                if (_count == _entries.Length)
                {
                    Resize(_count * 2);
                    bucket = ref _buckets[hash & (_buckets.Length - 1)];
                }

                entryIndex = _count++;
            }

            ref Entry entry = ref _entries[entryIndex];
            entry.HashCode = hash;
            entry.Key = key;
            entry.Value = value;
            entry.Next = bucket - 1;
            bucket = entryIndex + 1;
            return true;
        }

        public bool TryGetValue(TKey key, out int value)
        {
            int hash = HashOf(key);
            for (int index = _buckets[hash & (_buckets.Length - 1)] - 1; index >= 0; index = _entries[index].Next)
            {
                if (_entries[index].HashCode == hash && ReferenceEquals(_entries[index].Key, key))
                {
                    value = _entries[index].Value;
                    return true;
                }
            }

            value = 0;
            return false;
        }

        public bool ContainsKey(TKey key)
        {
            int hash = HashOf(key);
            for (int index = _buckets[hash & (_buckets.Length - 1)] - 1; index >= 0; index = _entries[index].Next)
            {
                if (_entries[index].HashCode == hash && ReferenceEquals(_entries[index].Key, key))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Overwrites the value of an existing key. Returns false when the key is absent.</summary>
        public bool TrySetValue(TKey key, int value)
        {
            int hash = HashOf(key);
            for (int index = _buckets[hash & (_buckets.Length - 1)] - 1; index >= 0; index = _entries[index].Next)
            {
                if (_entries[index].HashCode == hash && ReferenceEquals(_entries[index].Key, key))
                {
                    _entries[index].Value = value;
                    return true;
                }
            }

            return false;
        }

        public bool Remove(TKey key)
        {
            int hash = HashOf(key);
            int bucketIndex = hash & (_buckets.Length - 1);
            int previous = -1;

            for (int index = _buckets[bucketIndex] - 1; index >= 0; index = _entries[index].Next)
            {
                if (_entries[index].HashCode == hash && ReferenceEquals(_entries[index].Key, key))
                {
                    if (previous < 0)
                    {
                        _buckets[bucketIndex] = _entries[index].Next + 1;
                    }
                    else
                    {
                        _entries[previous].Next = _entries[index].Next;
                    }

                    ref Entry entry = ref _entries[index];
                    entry.HashCode = -1;
                    entry.Next = _freeList;
                    entry.Key = null;
                    entry.Value = 0;
                    _freeList = index;
                    _freeCount++;
                    return true;
                }

                previous = index;
            }

            return false;
        }

        /// <summary>Reserves room for <paramref name="capacity"/> live entries without touching items.</summary>
        public void EnsureCapacity(int capacity)
        {
            if (capacity <= _entries.Length)
            {
                return;
            }

            Resize(RoundUpToPowerOfTwo(capacity));
        }

        public void Clear()
        {
            Array.Clear(_buckets, 0, _buckets.Length);
            Array.Clear(_entries, 0, _count);
            _count = 0;
            _freeList = -1;
            _freeCount = 0;
        }

        /// <summary>Drops the entry and bucket storage of an empty map. Allocates the minimum arrays.</summary>
        public void ReleaseCapacity()
        {
            if (_count == 0 && _buckets.Length == MinimumCapacity)
            {
                return;
            }

            _buckets = new int[MinimumCapacity];
            _entries = new Entry[MinimumCapacity];
            _count = 0;
            _freeList = -1;
            _freeCount = 0;
        }

        private void Resize(int newSize)
        {
            var newBuckets = new int[newSize];
            var newEntries = new Entry[newSize];

            // Entry indices are preserved so free-list links and existing chains stay coherent.
            Array.Copy(_entries, newEntries, _count);

            for (int index = 0; index < _count; index++)
            {
                if (newEntries[index].HashCode >= 0)
                {
                    int bucketIndex = newEntries[index].HashCode & (newSize - 1);
                    newEntries[index].Next = newBuckets[bucketIndex] - 1;
                    newBuckets[bucketIndex] = index + 1;
                }
            }

            _buckets = newBuckets;
            _entries = newEntries;
        }

        private static int HashOf(TKey key)
        {
            int hash = RuntimeHelpers.GetHashCode(key);

            // Spread bits so power-of-two masking stays uniform for sequential runtime hashes.
            hash ^= hash >> 16;
            hash = (int)((uint)hash * 0x7FEB352Du);
            hash ^= hash >> 15;
            return hash & 0x7FFFFFFF;
        }

        private static int RoundUpToPowerOfTwo(int value)
        {
            const int MaxSize = 1 << 30;
            if (value >= MaxSize)
            {
                return MaxSize;
            }

            int result = MinimumCapacity;
            while (result < value)
            {
                result <<= 1;
            }

            return result;
        }
    }
}
