#if PRESENT_COLLECTIONS
using System;
using Unity.Collections;

namespace CycloneGames.Factory.DOD.Runtime
{
    /// <summary>
    /// Compact, cache-friendly pool backed by NativeArray for DOD/Jobs patterns.
    /// Active items occupy contiguous memory at [0..ActiveCount). Swap-and-pop despawn, O(1).
    /// Fully unmanaged, Burst-compatible, zero GC.
    /// </summary>
    public struct NativePool<T> : IDisposable where T : unmanaged
    {
        private NativeArray<T> _data;
        private int _activeCount;

        public NativePool(int capacity, Allocator allocator)
        {
            _data = new NativeArray<T>(capacity, allocator);
            _activeCount = 0;
        }

        public readonly int ActiveCount => _activeCount;
        public readonly int Capacity => _data.Length;
        public readonly bool IsCreated => _data.IsCreated;

        /// <summary>
        /// The underlying NativeArray. Pass to Jobs with [ReadOnly]/[WriteOnly].
        /// Only indices [0..ActiveCount) contain valid active items.
        /// </summary>
        public readonly NativeArray<T> RawArray => _data;

        /// <summary>
        /// Sub-array alias of active items only. Usable in IJobParallelFor.
        /// </summary>
        public readonly NativeArray<T> ActiveItems => _data.GetSubArray(0, _activeCount);

        /// <summary>
        /// Activates a new item at the end of the compact region.
        /// Returns its index, or -1 if pool is at capacity.
        /// </summary>
        public int Spawn(in T item)
        {
            if (_activeCount >= _data.Length) return -1;
            int index = _activeCount++;
            _data[index] = item;
            return index;
        }

        /// <summary>
        /// Spawns multiple items in bulk. Returns the index of the first spawned item,
        /// or -1 if not enough capacity. Allows partial fill when allowPartial is true.
        /// </summary>
        public int SpawnBatch(NativeArray<T> items, int count, bool allowPartial = false)
        {
            int available = _data.Length - _activeCount;
            if (!allowPartial && count > available) return -1;
            int toSpawn = Math.Min(count, available);
            if (toSpawn <= 0) return -1;

            int startIndex = _activeCount;
            NativeArray<T>.Copy(items, 0, _data, _activeCount, toSpawn);
            _activeCount += toSpawn;
            return startIndex;
        }

        /// <summary>
        /// Deactivates item at index via swap-and-pop. O(1).
        /// Returns the original index of the item swapped into the gap,
        /// or -1 if no swap occurred (despawned item was already last).
        /// Caller must update any external index references for the swapped item.
        /// </summary>
        public int Despawn(int index)
        {
            if ((uint)index >= (uint)_activeCount) return -1;
            _activeCount--;
            if (index < _activeCount)
            {
                _data[index] = _data[_activeCount];
                return _activeCount;
            }
            return -1;
        }

        /// <summary>
        /// Batch despawn: removes all items where the predicate mask is true.
        /// More efficient than individual Despawn calls for bulk removal.
        /// Compacts remaining items. Returns the new active count.
        /// </summary>
        public int DespawnBatch(NativeArray<bool> despawnMask)
        {
            int writePos = 0;
            for (int readPos = 0; readPos < _activeCount; readPos++)
            {
                if (!despawnMask[readPos])
                {
                    if (writePos != readPos)
                    {
                        _data[writePos] = _data[readPos];
                    }
                    writePos++;
                }
            }
            _activeCount = writePos;
            return _activeCount;
        }

        public T this[int index]
        {
            readonly get => _data[index];
            set => _data[index] = value;
        }

        public void Clear() => _activeCount = 0;

        public void Dispose()
        {
            if (_data.IsCreated) _data.Dispose();
            _activeCount = 0;
        }
    }
}
#endif
