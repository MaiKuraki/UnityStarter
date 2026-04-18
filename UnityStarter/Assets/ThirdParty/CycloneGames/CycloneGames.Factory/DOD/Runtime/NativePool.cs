#if PRESENT_COLLECTIONS
using System;
using Unity.Collections;

namespace CycloneGames.Factory.DOD.Runtime
{
    public readonly struct NativeDenseDiagnostics
    {
        public int PeakCountActive { get; }
        public int TotalSpawned { get; }
        public int TotalDespawned { get; }
        public int RejectedSpawns { get; }
        public int InvalidDespawns { get; }

        public NativeDenseDiagnostics(
            int peakCountActive,
            int totalSpawned,
            int totalDespawned,
            int rejectedSpawns,
            int invalidDespawns)
        {
            PeakCountActive = peakCountActive;
            TotalSpawned = totalSpawned;
            TotalDespawned = totalDespawned;
            RejectedSpawns = rejectedSpawns;
            InvalidDespawns = invalidDespawns;
        }
    }

    public readonly struct NativeDenseProfile
    {
        public int CountAll { get; }
        public int CountActive { get; }
        public int CountInactive { get; }
        public NativeDenseDiagnostics Diagnostics { get; }

        public NativeDenseProfile(
            int countAll,
            int countActive,
            int countInactive,
            NativeDenseDiagnostics diagnostics)
        {
            CountAll = countAll;
            CountActive = countActive;
            CountInactive = countInactive;
            Diagnostics = diagnostics;
        }
    }

    public interface INativeDenseHandlePool
    {
        int Capacity { get; }
        int CountAll { get; }
        int CountActive { get; }
        int CountInactive { get; }
        NativeDenseDiagnostics Diagnostics { get; }
        NativeDenseProfile Profile { get; }
        bool Contains(NativePoolHandle handle);
        bool TryGetDenseIndex(NativePoolHandle handle, out int denseIndex);
        NativePoolHandle GetHandleAtDenseIndex(int denseIndex);
        void Clear();
    }

    public interface INativeDenseColumnPool2<T0, T1> : INativeDenseHandlePool
        where T0 : unmanaged
        where T1 : unmanaged
    {
        NativeArray<T0> Stream0 { get; }
        NativeArray<T1> Stream1 { get; }
        bool TrySpawn(in T0 value0, in T1 value1, out NativePoolHandle handle, out int denseIndex);
        int SpawnBatch(NativeArray<T0> stream0Values, NativeArray<T1> stream1Values, int count, NativeArray<NativePoolHandle> handles, bool allowPartial = false);
        bool Despawn(NativePoolHandle handle);
        int DespawnBatch(NativeArray<NativePoolHandle> handles, int count);
        bool TryRead(NativePoolHandle handle, out T0 value0, out T1 value1);
        bool TryWrite(NativePoolHandle handle, in T0 value0, in T1 value1);
    }

    public interface INativeDenseColumnPool3<T0, T1, T2> : INativeDenseHandlePool
        where T0 : unmanaged
        where T1 : unmanaged
        where T2 : unmanaged
    {
        NativeArray<T0> Stream0 { get; }
        NativeArray<T1> Stream1 { get; }
        NativeArray<T2> Stream2 { get; }
        bool TrySpawn(in T0 value0, in T1 value1, in T2 value2, out NativePoolHandle handle, out int denseIndex);
        int SpawnBatch(NativeArray<T0> stream0Values, NativeArray<T1> stream1Values, NativeArray<T2> stream2Values, int count, NativeArray<NativePoolHandle> handles, bool allowPartial = false);
        bool Despawn(NativePoolHandle handle);
        int DespawnBatch(NativeArray<NativePoolHandle> handles, int count);
        bool TryRead(NativePoolHandle handle, out T0 value0, out T1 value1, out T2 value2);
        bool TryWrite(NativePoolHandle handle, in T0 value0, in T1 value1, in T2 value2);
    }

    public interface INativeDenseColumnPool4<T0, T1, T2, T3> : INativeDenseHandlePool
        where T0 : unmanaged
        where T1 : unmanaged
        where T2 : unmanaged
        where T3 : unmanaged
    {
        NativeArray<T0> Stream0 { get; }
        NativeArray<T1> Stream1 { get; }
        NativeArray<T2> Stream2 { get; }
        NativeArray<T3> Stream3 { get; }
        bool TrySpawn(in T0 value0, in T1 value1, in T2 value2, in T3 value3, out NativePoolHandle handle, out int denseIndex);
        int SpawnBatch(NativeArray<T0> stream0Values, NativeArray<T1> stream1Values, NativeArray<T2> stream2Values, NativeArray<T3> stream3Values, int count, NativeArray<NativePoolHandle> handles, bool allowPartial = false);
        bool Despawn(NativePoolHandle handle);
        int DespawnBatch(NativeArray<NativePoolHandle> handles, int count);
        bool TryRead(NativePoolHandle handle, out T0 value0, out T1 value1, out T2 value2, out T3 value3);
        bool TryWrite(NativePoolHandle handle, in T0 value0, in T1 value1, in T2 value2, in T3 value3);
    }

    internal struct NativeDenseIndexMap : IDisposable
    {
        private NativeArray<int> _denseToSlot;
        private NativeArray<int> _slotToDense;
        private NativeArray<int> _slotGenerations;
        private NativeArray<int> _freeSlots;
        private Allocator _allocator;

        public NativeDenseIndexMap(int capacity, Allocator allocator)
        {
            _allocator = allocator;
            _denseToSlot = new NativeArray<int>(capacity, allocator, NativeArrayOptions.UninitializedMemory);
            _slotToDense = new NativeArray<int>(capacity, allocator, NativeArrayOptions.UninitializedMemory);
            _slotGenerations = new NativeArray<int>(capacity, allocator, NativeArrayOptions.ClearMemory);
            _freeSlots = new NativeArray<int>(capacity, allocator, NativeArrayOptions.UninitializedMemory);

            for (int i = 0; i < capacity; i++)
            {
                _denseToSlot[i] = -1;
                _slotToDense[i] = -1;
                _freeSlots[i] = capacity - 1 - i;
            }
        }

        public readonly int Capacity => _denseToSlot.Length;

        public bool TryAllocate(ref int activeCount, ref int freeCount, out NativePoolHandle handle, out int denseIndex)
        {
            if (freeCount <= 0)
            {
                handle = default;
                denseIndex = -1;
                return false;
            }

            int freeIndex = --freeCount;
            int slot = _freeSlots[freeIndex];
            int generation = _slotGenerations[slot];
            if (generation <= 0)
            {
                generation = 1;
            }

            denseIndex = activeCount++;
            _denseToSlot[denseIndex] = slot;
            _slotToDense[slot] = denseIndex;
            _slotGenerations[slot] = generation;
            handle = new NativePoolHandle(slot, generation);
            return true;
        }

        public bool TryRelease(NativePoolHandle handle, ref int activeCount, ref int freeCount, out int denseIndex, out int lastDenseIndex)
        {
            if (!Contains(handle, activeCount))
            {
                denseIndex = -1;
                lastDenseIndex = -1;
                return false;
            }

            int slot = handle.Slot;
            denseIndex = _slotToDense[slot];
            lastDenseIndex = activeCount - 1;
            int swappedSlot = _denseToSlot[lastDenseIndex];

            activeCount--;
            if (denseIndex != lastDenseIndex)
            {
                _denseToSlot[denseIndex] = swappedSlot;
                _slotToDense[swappedSlot] = denseIndex;
            }

            _denseToSlot[lastDenseIndex] = -1;
            _slotToDense[slot] = -1;
            _slotGenerations[slot] = _slotGenerations[slot] == int.MaxValue ? 1 : _slotGenerations[slot] + 1;
            _freeSlots[freeCount++] = slot;
            return true;
        }

        public readonly bool Contains(NativePoolHandle handle, int activeCount)
        {
            if (!handle.IsValid || (uint)handle.Slot >= (uint)_slotToDense.Length)
            {
                return false;
            }

            int denseIndex = _slotToDense[handle.Slot];
            return denseIndex >= 0
                && denseIndex < activeCount
                && _slotGenerations[handle.Slot] == handle.Generation;
        }

        public readonly bool TryGetDenseIndex(NativePoolHandle handle, int activeCount, out int denseIndex)
        {
            if (!Contains(handle, activeCount))
            {
                denseIndex = -1;
                return false;
            }

            denseIndex = _slotToDense[handle.Slot];
            return true;
        }

        public readonly NativePoolHandle GetHandleAtDenseIndex(int denseIndex, int activeCount)
        {
            if ((uint)denseIndex >= (uint)activeCount)
            {
                throw new ArgumentOutOfRangeException(nameof(denseIndex));
            }

            int slot = _denseToSlot[denseIndex];
            return new NativePoolHandle(slot, _slotGenerations[slot]);
        }

        /// <summary>
        /// Allocates multiple slots in bulk. Returns the number actually allocated.
        /// Dense indices are guaranteed contiguous: [startDenseIndex .. startDenseIndex + returned - 1].
        /// </summary>
        public int BulkAllocate(ref int activeCount, ref int freeCount, int requestCount, NativeArray<NativePoolHandle> handles, out int startDenseIndex)
        {
            int toAllocate = Math.Min(requestCount, freeCount);
            startDenseIndex = activeCount;

            for (int i = 0; i < toAllocate; i++)
            {
                int freeIndex = --freeCount;
                int slot = _freeSlots[freeIndex];
                int generation = _slotGenerations[slot];
                if (generation <= 0) generation = 1;

                int denseIndex = activeCount++;
                _denseToSlot[denseIndex] = slot;
                _slotToDense[slot] = denseIndex;
                _slotGenerations[slot] = generation;
                handles[i] = new NativePoolHandle(slot, generation);
            }

            return toAllocate;
        }

        /// <summary>
        /// Grows the index map to a larger capacity, preserving all existing mappings.
        /// </summary>
        public void Resize(int newCapacity, int activeCount, ref int freeCount)
        {
            int oldCapacity = Capacity;
            if (newCapacity <= oldCapacity)
                throw new ArgumentOutOfRangeException(nameof(newCapacity), "New capacity must be larger than current.");

            var newDenseToSlot = new NativeArray<int>(newCapacity, _allocator, NativeArrayOptions.UninitializedMemory);
            var newSlotToDense = new NativeArray<int>(newCapacity, _allocator, NativeArrayOptions.UninitializedMemory);
            var newSlotGenerations = new NativeArray<int>(newCapacity, _allocator, NativeArrayOptions.ClearMemory);
            var newFreeSlots = new NativeArray<int>(newCapacity, _allocator, NativeArrayOptions.UninitializedMemory);

            NativeArray<int>.Copy(_denseToSlot, 0, newDenseToSlot, 0, oldCapacity);
            NativeArray<int>.Copy(_slotToDense, 0, newSlotToDense, 0, oldCapacity);
            NativeArray<int>.Copy(_slotGenerations, 0, newSlotGenerations, 0, oldCapacity);

            for (int i = oldCapacity; i < newCapacity; i++)
            {
                newDenseToSlot[i] = -1;
                newSlotToDense[i] = -1;
            }

            // Rebuild free list: keep existing free slots, append new slots
            NativeArray<int>.Copy(_freeSlots, 0, newFreeSlots, 0, freeCount);
            int addedFreeCount = newCapacity - oldCapacity;
            for (int i = 0; i < addedFreeCount; i++)
            {
                newFreeSlots[freeCount + i] = newCapacity - 1 - i;
            }
            freeCount += addedFreeCount;

            _denseToSlot.Dispose();
            _slotToDense.Dispose();
            _slotGenerations.Dispose();
            _freeSlots.Dispose();

            _denseToSlot = newDenseToSlot;
            _slotToDense = newSlotToDense;
            _slotGenerations = newSlotGenerations;
            _freeSlots = newFreeSlots;
        }

        public void Clear(ref int activeCount, ref int freeCount)
        {
            int capacity = Capacity;
            activeCount = 0;
            freeCount = capacity;

            for (int i = 0; i < capacity; i++)
            {
                _denseToSlot[i] = -1;
                _slotToDense[i] = -1;
                _slotGenerations[i] = _slotGenerations[i] <= 0 ? 1 : _slotGenerations[i] + 1;
                _freeSlots[i] = capacity - 1 - i;
            }
        }

        public void Dispose()
        {
            if (_denseToSlot.IsCreated) _denseToSlot.Dispose();
            if (_slotToDense.IsCreated) _slotToDense.Dispose();
            if (_slotGenerations.IsCreated) _slotGenerations.Dispose();
            if (_freeSlots.IsCreated) _freeSlots.Dispose();
        }
    }

    /// <summary>
    /// Stable handle for dense pools. Slot is stable, while dense indices are compact and may move.
    /// </summary>
    public readonly struct NativePoolHandle : IEquatable<NativePoolHandle>
    {
        public int Slot { get; }
        public int Generation { get; }

        public NativePoolHandle(int slot, int generation)
        {
            Slot = slot;
            Generation = generation;
        }

        public bool IsValid => Slot >= 0 && Generation > 0;

        public bool Equals(NativePoolHandle other)
        {
            return Slot == other.Slot && Generation == other.Generation;
        }

        public override bool Equals(object obj)
        {
            return obj is NativePoolHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                uint h = (uint)Slot;
                h = ((h >> 16) ^ h) * 0x45d9f3b;
                h = ((h >> 16) ^ h) * 0x45d9f3b;
                h = (h >> 16) ^ h;
                return (int)(h ^ (uint)Generation * 0x9e3779b9);
            }
        }
    }

    /// <summary>
    /// Compact, cache-friendly pool backed by NativeArray for DOD/Jobs patterns.
    /// Active items occupy contiguous memory at [0..ActiveCount). Swap-and-pop despawn, O(1).
    /// Fully unmanaged, Burst-compatible, zero GC.
    /// <para>
    /// <b>Thread Safety:</b> Spawn/Despawn must be called from the main thread.
    /// <see cref="RawArray"/> and <see cref="ActiveItems"/> can be passed to IJob / IJobParallelFor.
    /// On WebGL, Jobs execute synchronously with no parallelism benefit.
    /// </para>
    /// </summary>
    public struct NativePool<T> : IDisposable where T : unmanaged
    {
        private NativeArray<T> _data;
        private Allocator _allocator;
        private int _activeCount;

        public NativePool(int capacity, Allocator allocator)
        {
            _data = new NativeArray<T>(capacity, allocator);
            _allocator = allocator;
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
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!_data.IsCreated) throw new ObjectDisposedException(nameof(NativePool<T>));
#endif
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
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!_data.IsCreated) throw new ObjectDisposedException(nameof(NativePool<T>));
#endif
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
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!_data.IsCreated) throw new ObjectDisposedException(nameof(NativePool<T>));
#endif
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
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!_data.IsCreated) throw new ObjectDisposedException(nameof(NativePool<T>));
#endif
            if (despawnMask.Length < _activeCount)
                throw new ArgumentException($"Despawn mask length ({despawnMask.Length}) is less than active count ({_activeCount}).", nameof(despawnMask));

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

        /// <summary>
        /// Grows the pool to a larger capacity, preserving all active items.
        /// </summary>
        public void Resize(int newCapacity)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!_data.IsCreated) throw new ObjectDisposedException(nameof(NativePool<T>));
#endif
            if (newCapacity <= _data.Length)
                throw new ArgumentOutOfRangeException(nameof(newCapacity), "New capacity must be larger than current.");

            var newData = new NativeArray<T>(newCapacity, _allocator, NativeArrayOptions.UninitializedMemory);
            if (_activeCount > 0)
                NativeArray<T>.Copy(_data, 0, newData, 0, _activeCount);
            _data.Dispose();
            _data = newData;
        }

        public void Clear() => _activeCount = 0;

        public void Dispose()
        {
            if (_data.IsCreated) _data.Dispose();
            _activeCount = 0;
        }
    }

    /// <summary>
    /// Handle-based dense pool for unmanaged high-density simulations.
    /// Active items live in contiguous dense memory for cache-friendly iteration,
    /// while stable handles protect against stale references and double-despawn.
    /// <para>
    /// <b>Thread Safety:</b> Spawn/Despawn must be called from the main thread only.
    /// Only <see cref="ActiveItems"/> is safe to pass into IJob / IJobParallelFor for read/write.
    /// On WebGL (single-threaded), Jobs execute synchronously with no parallelism benefit.
    /// </para>
    /// </summary>
    public struct NativeDensePool<T> : IDisposable where T : unmanaged
    {
        private NativeArray<T> _denseItems;
        private NativeDenseIndexMap _indexMap;
        private Allocator _allocator;
        private int _activeCount;
        private int _freeCount;
        private int _peakCountActive;
        private int _totalSpawned;
        private int _totalDespawned;
        private int _rejectedSpawns;
        private int _invalidDespawns;

        public NativeDensePool(int capacity, Allocator allocator, NativeArrayOptions options = NativeArrayOptions.ClearMemory)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _denseItems = new NativeArray<T>(capacity, allocator, options);
            _indexMap = new NativeDenseIndexMap(capacity, allocator);
            _allocator = allocator;
            _activeCount = 0;
            _freeCount = capacity;
            _peakCountActive = 0;
            _totalSpawned = 0;
            _totalDespawned = 0;
            _rejectedSpawns = 0;
            _invalidDespawns = 0;
        }

        public readonly bool IsCreated => _denseItems.IsCreated;
        public readonly int Capacity => _denseItems.Length;
        public readonly int CountAll => Capacity;
        public readonly int CountActive => _activeCount;
        public readonly int CountInactive => _freeCount;
        public readonly NativeDenseDiagnostics Diagnostics => new(
            _peakCountActive,
            _totalSpawned,
            _totalDespawned,
            _rejectedSpawns,
            _invalidDespawns);
        public readonly NativeDenseProfile Profile => new(CountAll, CountActive, CountInactive, Diagnostics);
        public readonly NativeArray<T> ActiveItems => _denseItems.GetSubArray(0, _activeCount);

        public bool TrySpawn(in T value, out NativePoolHandle handle, out int denseIndex)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!_denseItems.IsCreated) throw new ObjectDisposedException(nameof(NativeDensePool<T>));
#endif
            if (!_indexMap.TryAllocate(ref _activeCount, ref _freeCount, out handle, out denseIndex))
            {
                _rejectedSpawns++;
                return false;
            }

            _denseItems[denseIndex] = value;
            _totalSpawned++;
            if (_activeCount > _peakCountActive)
            {
                _peakCountActive = _activeCount;
            }
            return true;
        }

        public int SpawnBatch(NativeArray<T> values, int count, NativeArray<NativePoolHandle> handles, bool allowPartial = false)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!_denseItems.IsCreated) throw new ObjectDisposedException(nameof(NativeDensePool<T>));
#endif
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (count > values.Length) throw new ArgumentOutOfRangeException(nameof(count));
            if (handles.Length < count && !allowPartial) throw new ArgumentException("Handle output array is too small.", nameof(handles));

            int available = _freeCount;
            if (!allowPartial && count > available)
            {
                return 0;
            }

            int toSpawn = Math.Min(Math.Min(count, available), handles.Length);
            int spawned = _indexMap.BulkAllocate(ref _activeCount, ref _freeCount, toSpawn, handles, out int startDense);
            if (spawned > 0)
            {
                NativeArray<T>.Copy(values, 0, _denseItems, startDense, spawned);
            }

            _totalSpawned += spawned;
            if (_activeCount > _peakCountActive)
            {
                _peakCountActive = _activeCount;
            }

            return spawned;
        }

        public bool Despawn(NativePoolHandle handle)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!_denseItems.IsCreated) throw new ObjectDisposedException(nameof(NativeDensePool<T>));
#endif
            if (!_indexMap.TryRelease(handle, ref _activeCount, ref _freeCount, out int denseIndex, out int lastDenseIndex))
            {
                _invalidDespawns++;
                return false;
            }
            if (denseIndex != lastDenseIndex)
            {
                _denseItems[denseIndex] = _denseItems[lastDenseIndex];
            }
            _totalDespawned++;
            return true;
        }

        public int DespawnBatch(NativeArray<NativePoolHandle> handles, int count)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!_denseItems.IsCreated) throw new ObjectDisposedException(nameof(NativeDensePool<T>));
#endif
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (count > handles.Length) throw new ArgumentOutOfRangeException(nameof(count));

            int despawned = 0;
            for (int i = 0; i < count; i++)
            {
                if (Despawn(handles[i]))
                {
                    despawned++;
                }
            }

            return despawned;
        }

        public readonly bool Contains(NativePoolHandle handle)
        {
            return _denseItems.IsCreated && _indexMap.Contains(handle, _activeCount);
        }

        public readonly bool TryGetDenseIndex(NativePoolHandle handle, out int denseIndex)
        {
            if (!_denseItems.IsCreated)
            {
                denseIndex = -1;
                return false;
            }

            return _indexMap.TryGetDenseIndex(handle, _activeCount, out denseIndex);
        }

        public readonly bool TryRead(NativePoolHandle handle, out T value)
        {
            if (TryGetDenseIndex(handle, out int denseIndex))
            {
                value = _denseItems[denseIndex];
                return true;
            }

            value = default;
            return false;
        }

        public bool TryWrite(NativePoolHandle handle, in T value)
        {
            if (!TryGetDenseIndex(handle, out int denseIndex))
            {
                return false;
            }

            _denseItems[denseIndex] = value;
            return true;
        }

        public readonly NativePoolHandle GetHandleAtDenseIndex(int denseIndex)
        {
            return _indexMap.GetHandleAtDenseIndex(denseIndex, _activeCount);
        }

        /// <summary>
        /// Grows the pool to a larger capacity, preserving all active items and handles.
        /// </summary>
        public void Resize(int newCapacity)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!_denseItems.IsCreated) throw new ObjectDisposedException(nameof(NativeDensePool<T>));
#endif
            if (newCapacity <= Capacity)
                throw new ArgumentOutOfRangeException(nameof(newCapacity), "New capacity must be larger than current.");

            var newItems = new NativeArray<T>(newCapacity, _allocator, NativeArrayOptions.UninitializedMemory);
            if (_activeCount > 0)
                NativeArray<T>.Copy(_denseItems, 0, newItems, 0, _activeCount);
            _denseItems.Dispose();
            _denseItems = newItems;

            _indexMap.Resize(newCapacity, _activeCount, ref _freeCount);
        }

        public void Clear()
        {
            _indexMap.Clear(ref _activeCount, ref _freeCount);
        }

        public void Dispose()
        {
            if (_denseItems.IsCreated) _denseItems.Dispose();
            _indexMap.Dispose();
            _activeCount = 0;
            _freeCount = 0;
        }
    }

    /// <summary>
    /// Two-stream SoA dense pool with stable handles.
    /// Keeps tightly packed parallel arrays for cache-friendly iteration across separate data columns.
    /// </summary>
    public struct NativeDenseColumnPool2<T0, T1> : INativeDenseColumnPool2<T0, T1>, IDisposable
        where T0 : unmanaged
        where T1 : unmanaged
    {
        private NativeArray<T0> _stream0;
        private NativeArray<T1> _stream1;
        private NativeDenseIndexMap _indexMap;
        private Allocator _allocator;
        private int _activeCount;
        private int _freeCount;
        private int _peakCountActive;
        private int _totalSpawned;
        private int _totalDespawned;
        private int _rejectedSpawns;
        private int _invalidDespawns;

        public NativeDenseColumnPool2(int capacity, Allocator allocator, NativeArrayOptions options = NativeArrayOptions.ClearMemory)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _stream0 = new NativeArray<T0>(capacity, allocator, options);
            _stream1 = new NativeArray<T1>(capacity, allocator, options);
            _indexMap = new NativeDenseIndexMap(capacity, allocator);
            _allocator = allocator;
            _activeCount = 0;
            _freeCount = capacity;
            _peakCountActive = 0;
            _totalSpawned = 0;
            _totalDespawned = 0;
            _rejectedSpawns = 0;
            _invalidDespawns = 0;
        }

        public readonly int Capacity => _stream0.Length;
        public readonly int CountAll => Capacity;
        public readonly int CountActive => _activeCount;
        public readonly int CountInactive => _freeCount;
        public readonly NativeDenseDiagnostics Diagnostics => new(
            _peakCountActive,
            _totalSpawned,
            _totalDespawned,
            _rejectedSpawns,
            _invalidDespawns);
        public readonly NativeDenseProfile Profile => new(CountAll, CountActive, CountInactive, Diagnostics);
        public readonly NativeArray<T0> Stream0 => _stream0.GetSubArray(0, _activeCount);
        public readonly NativeArray<T1> Stream1 => _stream1.GetSubArray(0, _activeCount);

        public bool TrySpawn(in T0 value0, in T1 value1, out NativePoolHandle handle, out int denseIndex)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!_stream0.IsCreated) throw new ObjectDisposedException(nameof(NativeDenseColumnPool2<T0, T1>));
#endif
            if (!_indexMap.TryAllocate(ref _activeCount, ref _freeCount, out handle, out denseIndex))
            {
                _rejectedSpawns++;
                return false;
            }
            _stream0[denseIndex] = value0;
            _stream1[denseIndex] = value1;
            _totalSpawned++;
            if (_activeCount > _peakCountActive)
            {
                _peakCountActive = _activeCount;
            }
            return true;
        }

        public int SpawnBatch(NativeArray<T0> stream0Values, NativeArray<T1> stream1Values, int count, NativeArray<NativePoolHandle> handles, bool allowPartial = false)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!_stream0.IsCreated) throw new ObjectDisposedException(nameof(NativeDenseColumnPool2<T0, T1>));
#endif
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (count > stream0Values.Length || count > stream1Values.Length) throw new ArgumentOutOfRangeException(nameof(count));
            if (handles.Length < count && !allowPartial) throw new ArgumentException("Handle output array is too small.", nameof(handles));

            int available = _freeCount;
            if (!allowPartial && count > available) return 0;

            int toSpawn = Math.Min(Math.Min(count, available), handles.Length);
            int spawned = _indexMap.BulkAllocate(ref _activeCount, ref _freeCount, toSpawn, handles, out int startDense);
            if (spawned > 0)
            {
                NativeArray<T0>.Copy(stream0Values, 0, _stream0, startDense, spawned);
                NativeArray<T1>.Copy(stream1Values, 0, _stream1, startDense, spawned);
            }

            _totalSpawned += spawned;
            if (_activeCount > _peakCountActive)
            {
                _peakCountActive = _activeCount;
            }

            return spawned;
        }

        public bool Despawn(NativePoolHandle handle)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!_stream0.IsCreated) throw new ObjectDisposedException(nameof(NativeDenseColumnPool2<T0, T1>));
#endif
            if (!_indexMap.TryRelease(handle, ref _activeCount, ref _freeCount, out int denseIndex, out int lastDenseIndex))
            {
                _invalidDespawns++;
                return false;
            }
            if (denseIndex != lastDenseIndex)
            {
                _stream0[denseIndex] = _stream0[lastDenseIndex];
                _stream1[denseIndex] = _stream1[lastDenseIndex];
            }
            _totalDespawned++;
            return true;
        }

        public int DespawnBatch(NativeArray<NativePoolHandle> handles, int count)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!_stream0.IsCreated) throw new ObjectDisposedException(nameof(NativeDenseColumnPool2<T0, T1>));
#endif
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (count > handles.Length) throw new ArgumentOutOfRangeException(nameof(count));

            int despawned = 0;
            for (int i = 0; i < count; i++)
            {
                if (Despawn(handles[i]))
                {
                    despawned++;
                }
            }

            return despawned;
        }

        public readonly bool Contains(NativePoolHandle handle)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!_stream0.IsCreated) throw new ObjectDisposedException(nameof(NativeDenseColumnPool2<T0, T1>));
#endif
            return _indexMap.Contains(handle, _activeCount);
        }

        public readonly bool TryGetDenseIndex(NativePoolHandle handle, out int denseIndex)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!_stream0.IsCreated) throw new ObjectDisposedException(nameof(NativeDenseColumnPool2<T0, T1>));
#endif
            return _indexMap.TryGetDenseIndex(handle, _activeCount, out denseIndex);
        }

        public bool TryRead(NativePoolHandle handle, out T0 value0, out T1 value1)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!_stream0.IsCreated) throw new ObjectDisposedException(nameof(NativeDenseColumnPool2<T0, T1>));
#endif
            if (TryGetDenseIndex(handle, out int denseIndex))
            {
                value0 = _stream0[denseIndex];
                value1 = _stream1[denseIndex];
                return true;
            }

            value0 = default;
            value1 = default;
            return false;
        }

        public bool TryWrite(NativePoolHandle handle, in T0 value0, in T1 value1)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!_stream0.IsCreated) throw new ObjectDisposedException(nameof(NativeDenseColumnPool2<T0, T1>));
#endif
            if (!TryGetDenseIndex(handle, out int denseIndex))
            {
                return false;
            }

            _stream0[denseIndex] = value0;
            _stream1[denseIndex] = value1;
            return true;
        }

        public readonly NativePoolHandle GetHandleAtDenseIndex(int denseIndex)
        {
            return _indexMap.GetHandleAtDenseIndex(denseIndex, _activeCount);
        }

        public void Resize(int newCapacity)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!_stream0.IsCreated) throw new ObjectDisposedException(nameof(NativeDenseColumnPool2<T0, T1>));
#endif
            if (newCapacity <= Capacity)
                throw new ArgumentOutOfRangeException(nameof(newCapacity), "New capacity must be larger than current.");

            var ns0 = new NativeArray<T0>(newCapacity, _allocator, NativeArrayOptions.UninitializedMemory);
            var ns1 = new NativeArray<T1>(newCapacity, _allocator, NativeArrayOptions.UninitializedMemory);
            if (_activeCount > 0)
            {
                NativeArray<T0>.Copy(_stream0, 0, ns0, 0, _activeCount);
                NativeArray<T1>.Copy(_stream1, 0, ns1, 0, _activeCount);
            }
            _stream0.Dispose(); _stream1.Dispose();
            _stream0 = ns0; _stream1 = ns1;
            _indexMap.Resize(newCapacity, _activeCount, ref _freeCount);
        }

        public void Clear()
        {
            _indexMap.Clear(ref _activeCount, ref _freeCount);
        }

        public void Dispose()
        {
            if (_stream0.IsCreated) _stream0.Dispose();
            if (_stream1.IsCreated) _stream1.Dispose();
            _indexMap.Dispose();
            _activeCount = 0;
            _freeCount = 0;
        }
    }

    /// <summary>
    /// Three-stream SoA dense pool with stable handles.
    /// Useful when high-frequency simulation data naturally splits into three hot columns.
    /// </summary>
    public struct NativeDenseColumnPool3<T0, T1, T2> : INativeDenseColumnPool3<T0, T1, T2>, IDisposable
        where T0 : unmanaged
        where T1 : unmanaged
        where T2 : unmanaged
    {
        private NativeArray<T0> _stream0;
        private NativeArray<T1> _stream1;
        private NativeArray<T2> _stream2;
        private NativeDenseIndexMap _indexMap;
        private Allocator _allocator;
        private int _activeCount;
        private int _freeCount;
        private int _peakCountActive;
        private int _totalSpawned;
        private int _totalDespawned;
        private int _rejectedSpawns;
        private int _invalidDespawns;

        public NativeDenseColumnPool3(int capacity, Allocator allocator, NativeArrayOptions options = NativeArrayOptions.ClearMemory)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));

            _stream0 = new NativeArray<T0>(capacity, allocator, options);
            _stream1 = new NativeArray<T1>(capacity, allocator, options);
            _stream2 = new NativeArray<T2>(capacity, allocator, options);
            _indexMap = new NativeDenseIndexMap(capacity, allocator);
            _allocator = allocator;
            _activeCount = 0;
            _freeCount = capacity;
            _peakCountActive = 0;
            _totalSpawned = 0;
            _totalDespawned = 0;
            _rejectedSpawns = 0;
            _invalidDespawns = 0;
        }

        public readonly int Capacity => _stream0.Length;
        public readonly int CountAll => Capacity;
        public readonly int CountActive => _activeCount;
        public readonly int CountInactive => _freeCount;
        public readonly NativeDenseDiagnostics Diagnostics => new(
            _peakCountActive,
            _totalSpawned,
            _totalDespawned,
            _rejectedSpawns,
            _invalidDespawns);
        public readonly NativeDenseProfile Profile => new(CountAll, CountActive, CountInactive, Diagnostics);
        public readonly NativeArray<T0> Stream0 => _stream0.GetSubArray(0, _activeCount);
        public readonly NativeArray<T1> Stream1 => _stream1.GetSubArray(0, _activeCount);
        public readonly NativeArray<T2> Stream2 => _stream2.GetSubArray(0, _activeCount);

        public bool TrySpawn(in T0 value0, in T1 value1, in T2 value2, out NativePoolHandle handle, out int denseIndex)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!_stream0.IsCreated) throw new ObjectDisposedException(nameof(NativeDenseColumnPool3<T0, T1, T2>));
#endif
            if (!_indexMap.TryAllocate(ref _activeCount, ref _freeCount, out handle, out denseIndex))
            {
                _rejectedSpawns++;
                return false;
            }
            _stream0[denseIndex] = value0;
            _stream1[denseIndex] = value1;
            _stream2[denseIndex] = value2;
            _totalSpawned++;
            if (_activeCount > _peakCountActive)
            {
                _peakCountActive = _activeCount;
            }
            return true;
        }

        public int SpawnBatch(NativeArray<T0> stream0Values, NativeArray<T1> stream1Values, NativeArray<T2> stream2Values, int count, NativeArray<NativePoolHandle> handles, bool allowPartial = false)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!_stream0.IsCreated) throw new ObjectDisposedException(nameof(NativeDenseColumnPool3<T0, T1, T2>));
#endif
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (count > stream0Values.Length || count > stream1Values.Length || count > stream2Values.Length) throw new ArgumentOutOfRangeException(nameof(count));
            if (handles.Length < count && !allowPartial) throw new ArgumentException("Handle output array is too small.", nameof(handles));

            int available = _freeCount;
            if (!allowPartial && count > available) return 0;

            int toSpawn = Math.Min(Math.Min(count, available), handles.Length);
            int spawned = _indexMap.BulkAllocate(ref _activeCount, ref _freeCount, toSpawn, handles, out int startDense);
            if (spawned > 0)
            {
                NativeArray<T0>.Copy(stream0Values, 0, _stream0, startDense, spawned);
                NativeArray<T1>.Copy(stream1Values, 0, _stream1, startDense, spawned);
                NativeArray<T2>.Copy(stream2Values, 0, _stream2, startDense, spawned);
            }

            _totalSpawned += spawned;
            if (_activeCount > _peakCountActive)
            {
                _peakCountActive = _activeCount;
            }

            return spawned;
        }

        public bool Despawn(NativePoolHandle handle)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!_stream0.IsCreated) throw new ObjectDisposedException(nameof(NativeDenseColumnPool3<T0, T1, T2>));
#endif
            if (!_indexMap.TryRelease(handle, ref _activeCount, ref _freeCount, out int denseIndex, out int lastDenseIndex))
            {
                _invalidDespawns++;
                return false;
            }
            if (denseIndex != lastDenseIndex)
            {
                _stream0[denseIndex] = _stream0[lastDenseIndex];
                _stream1[denseIndex] = _stream1[lastDenseIndex];
                _stream2[denseIndex] = _stream2[lastDenseIndex];
            }
            _totalDespawned++;
            return true;
        }

        public int DespawnBatch(NativeArray<NativePoolHandle> handles, int count)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!_stream0.IsCreated) throw new ObjectDisposedException(nameof(NativeDenseColumnPool3<T0, T1, T2>));
#endif
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (count > handles.Length) throw new ArgumentOutOfRangeException(nameof(count));

            int despawned = 0;
            for (int i = 0; i < count; i++)
            {
                if (Despawn(handles[i]))
                {
                    despawned++;
                }
            }

            return despawned;
        }

        public readonly bool Contains(NativePoolHandle handle)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!_stream0.IsCreated) throw new ObjectDisposedException(nameof(NativeDenseColumnPool3<T0, T1, T2>));
#endif
            return _indexMap.Contains(handle, _activeCount);
        }

        public readonly bool TryGetDenseIndex(NativePoolHandle handle, out int denseIndex)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!_stream0.IsCreated) throw new ObjectDisposedException(nameof(NativeDenseColumnPool3<T0, T1, T2>));
#endif
            return _indexMap.TryGetDenseIndex(handle, _activeCount, out denseIndex);
        }

        public bool TryRead(NativePoolHandle handle, out T0 value0, out T1 value1, out T2 value2)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!_stream0.IsCreated) throw new ObjectDisposedException(nameof(NativeDenseColumnPool3<T0, T1, T2>));
#endif
            if (TryGetDenseIndex(handle, out int denseIndex))
            {
                value0 = _stream0[denseIndex];
                value1 = _stream1[denseIndex];
                value2 = _stream2[denseIndex];
                return true;
            }

            value0 = default;
            value1 = default;
            value2 = default;
            return false;
        }

        public bool TryWrite(NativePoolHandle handle, in T0 value0, in T1 value1, in T2 value2)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!_stream0.IsCreated) throw new ObjectDisposedException(nameof(NativeDenseColumnPool3<T0, T1, T2>));
#endif
            if (!TryGetDenseIndex(handle, out int denseIndex))
            {
                return false;
            }

            _stream0[denseIndex] = value0;
            _stream1[denseIndex] = value1;
            _stream2[denseIndex] = value2;
            return true;
        }

        public readonly NativePoolHandle GetHandleAtDenseIndex(int denseIndex)
        {
            return _indexMap.GetHandleAtDenseIndex(denseIndex, _activeCount);
        }

        public void Resize(int newCapacity)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!_stream0.IsCreated) throw new ObjectDisposedException(nameof(NativeDenseColumnPool3<T0, T1, T2>));
#endif
            if (newCapacity <= Capacity)
                throw new ArgumentOutOfRangeException(nameof(newCapacity), "New capacity must be larger than current.");

            var ns0 = new NativeArray<T0>(newCapacity, _allocator, NativeArrayOptions.UninitializedMemory);
            var ns1 = new NativeArray<T1>(newCapacity, _allocator, NativeArrayOptions.UninitializedMemory);
            var ns2 = new NativeArray<T2>(newCapacity, _allocator, NativeArrayOptions.UninitializedMemory);
            if (_activeCount > 0)
            {
                NativeArray<T0>.Copy(_stream0, 0, ns0, 0, _activeCount);
                NativeArray<T1>.Copy(_stream1, 0, ns1, 0, _activeCount);
                NativeArray<T2>.Copy(_stream2, 0, ns2, 0, _activeCount);
            }
            _stream0.Dispose(); _stream1.Dispose(); _stream2.Dispose();
            _stream0 = ns0; _stream1 = ns1; _stream2 = ns2;
            _indexMap.Resize(newCapacity, _activeCount, ref _freeCount);
        }

        public void Clear()
        {
            _indexMap.Clear(ref _activeCount, ref _freeCount);
        }

        public void Dispose()
        {
            if (_stream0.IsCreated) _stream0.Dispose();
            if (_stream1.IsCreated) _stream1.Dispose();
            if (_stream2.IsCreated) _stream2.Dispose();
            _indexMap.Dispose();
            _activeCount = 0;
            _freeCount = 0;
        }
    }

    /// <summary>
    /// Four-stream SoA dense pool with stable handles.
    /// Useful when simulation hot data needs one more tightly packed stream without introducing wrapper structs.
    /// </summary>
    public struct NativeDenseColumnPool4<T0, T1, T2, T3> : INativeDenseColumnPool4<T0, T1, T2, T3>, IDisposable
        where T0 : unmanaged
        where T1 : unmanaged
        where T2 : unmanaged
        where T3 : unmanaged
    {
        private NativeArray<T0> _stream0;
        private NativeArray<T1> _stream1;
        private NativeArray<T2> _stream2;
        private NativeArray<T3> _stream3;
        private NativeDenseIndexMap _indexMap;
        private Allocator _allocator;
        private int _activeCount;
        private int _freeCount;
        private int _peakCountActive;
        private int _totalSpawned;
        private int _totalDespawned;
        private int _rejectedSpawns;
        private int _invalidDespawns;

        public NativeDenseColumnPool4(int capacity, Allocator allocator, NativeArrayOptions options = NativeArrayOptions.ClearMemory)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));

            _stream0 = new NativeArray<T0>(capacity, allocator, options);
            _stream1 = new NativeArray<T1>(capacity, allocator, options);
            _stream2 = new NativeArray<T2>(capacity, allocator, options);
            _stream3 = new NativeArray<T3>(capacity, allocator, options);
            _indexMap = new NativeDenseIndexMap(capacity, allocator);
            _allocator = allocator;
            _activeCount = 0;
            _freeCount = capacity;
            _peakCountActive = 0;
            _totalSpawned = 0;
            _totalDespawned = 0;
            _rejectedSpawns = 0;
            _invalidDespawns = 0;
        }

        public readonly int Capacity => _stream0.Length;
        public readonly int CountAll => Capacity;
        public readonly int CountActive => _activeCount;
        public readonly int CountInactive => _freeCount;
        public readonly NativeDenseDiagnostics Diagnostics => new(
            _peakCountActive,
            _totalSpawned,
            _totalDespawned,
            _rejectedSpawns,
            _invalidDespawns);
        public readonly NativeDenseProfile Profile => new(CountAll, CountActive, CountInactive, Diagnostics);
        public readonly NativeArray<T0> Stream0 => _stream0.GetSubArray(0, _activeCount);
        public readonly NativeArray<T1> Stream1 => _stream1.GetSubArray(0, _activeCount);
        public readonly NativeArray<T2> Stream2 => _stream2.GetSubArray(0, _activeCount);
        public readonly NativeArray<T3> Stream3 => _stream3.GetSubArray(0, _activeCount);

        public bool TrySpawn(in T0 value0, in T1 value1, in T2 value2, in T3 value3, out NativePoolHandle handle, out int denseIndex)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!_stream0.IsCreated) throw new ObjectDisposedException(nameof(NativeDenseColumnPool4<T0, T1, T2, T3>));
#endif
            if (!_indexMap.TryAllocate(ref _activeCount, ref _freeCount, out handle, out denseIndex))
            {
                _rejectedSpawns++;
                return false;
            }
            _stream0[denseIndex] = value0;
            _stream1[denseIndex] = value1;
            _stream2[denseIndex] = value2;
            _stream3[denseIndex] = value3;
            _totalSpawned++;
            if (_activeCount > _peakCountActive)
            {
                _peakCountActive = _activeCount;
            }
            return true;
        }

        public int SpawnBatch(NativeArray<T0> stream0Values, NativeArray<T1> stream1Values, NativeArray<T2> stream2Values, NativeArray<T3> stream3Values, int count, NativeArray<NativePoolHandle> handles, bool allowPartial = false)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!_stream0.IsCreated) throw new ObjectDisposedException(nameof(NativeDenseColumnPool4<T0, T1, T2, T3>));
#endif
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (count > stream0Values.Length || count > stream1Values.Length || count > stream2Values.Length || count > stream3Values.Length) throw new ArgumentOutOfRangeException(nameof(count));
            if (handles.Length < count && !allowPartial) throw new ArgumentException("Handle output array is too small.", nameof(handles));

            int available = _freeCount;
            if (!allowPartial && count > available) return 0;

            int toSpawn = Math.Min(Math.Min(count, available), handles.Length);
            int spawned = _indexMap.BulkAllocate(ref _activeCount, ref _freeCount, toSpawn, handles, out int startDense);
            if (spawned > 0)
            {
                NativeArray<T0>.Copy(stream0Values, 0, _stream0, startDense, spawned);
                NativeArray<T1>.Copy(stream1Values, 0, _stream1, startDense, spawned);
                NativeArray<T2>.Copy(stream2Values, 0, _stream2, startDense, spawned);
                NativeArray<T3>.Copy(stream3Values, 0, _stream3, startDense, spawned);
            }

            _totalSpawned += spawned;
            if (_activeCount > _peakCountActive)
            {
                _peakCountActive = _activeCount;
            }

            return spawned;
        }

        public bool Despawn(NativePoolHandle handle)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!_stream0.IsCreated) throw new ObjectDisposedException(nameof(NativeDenseColumnPool4<T0, T1, T2, T3>));
#endif
            if (!_indexMap.TryRelease(handle, ref _activeCount, ref _freeCount, out int denseIndex, out int lastDenseIndex))
            {
                _invalidDespawns++;
                return false;
            }
            if (denseIndex != lastDenseIndex)
            {
                _stream0[denseIndex] = _stream0[lastDenseIndex];
                _stream1[denseIndex] = _stream1[lastDenseIndex];
                _stream2[denseIndex] = _stream2[lastDenseIndex];
                _stream3[denseIndex] = _stream3[lastDenseIndex];
            }
            _totalDespawned++;
            return true;
        }

        public int DespawnBatch(NativeArray<NativePoolHandle> handles, int count)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!_stream0.IsCreated) throw new ObjectDisposedException(nameof(NativeDenseColumnPool4<T0, T1, T2, T3>));
#endif
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (count > handles.Length) throw new ArgumentOutOfRangeException(nameof(count));

            int despawned = 0;
            for (int i = 0; i < count; i++)
            {
                if (Despawn(handles[i]))
                {
                    despawned++;
                }
            }

            return despawned;
        }

        public readonly bool Contains(NativePoolHandle handle)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!_stream0.IsCreated) throw new ObjectDisposedException(nameof(NativeDenseColumnPool4<T0, T1, T2, T3>));
#endif
            return _indexMap.Contains(handle, _activeCount);
        }

        public readonly bool TryGetDenseIndex(NativePoolHandle handle, out int denseIndex)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!_stream0.IsCreated) throw new ObjectDisposedException(nameof(NativeDenseColumnPool4<T0, T1, T2, T3>));
#endif
            return _indexMap.TryGetDenseIndex(handle, _activeCount, out denseIndex);
        }

        public bool TryRead(NativePoolHandle handle, out T0 value0, out T1 value1, out T2 value2, out T3 value3)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!_stream0.IsCreated) throw new ObjectDisposedException(nameof(NativeDenseColumnPool4<T0, T1, T2, T3>));
#endif
            if (TryGetDenseIndex(handle, out int denseIndex))
            {
                value0 = _stream0[denseIndex];
                value1 = _stream1[denseIndex];
                value2 = _stream2[denseIndex];
                value3 = _stream3[denseIndex];
                return true;
            }

            value0 = default;
            value1 = default;
            value2 = default;
            value3 = default;
            return false;
        }

        public bool TryWrite(NativePoolHandle handle, in T0 value0, in T1 value1, in T2 value2, in T3 value3)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!_stream0.IsCreated) throw new ObjectDisposedException(nameof(NativeDenseColumnPool4<T0, T1, T2, T3>));
#endif
            if (!TryGetDenseIndex(handle, out int denseIndex))
            {
                return false;
            }

            _stream0[denseIndex] = value0;
            _stream1[denseIndex] = value1;
            _stream2[denseIndex] = value2;
            _stream3[denseIndex] = value3;
            return true;
        }

        public readonly NativePoolHandle GetHandleAtDenseIndex(int denseIndex)
        {
            return _indexMap.GetHandleAtDenseIndex(denseIndex, _activeCount);
        }

        public void Resize(int newCapacity)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            if (!_stream0.IsCreated) throw new ObjectDisposedException(nameof(NativeDenseColumnPool4<T0, T1, T2, T3>));
#endif
            if (newCapacity <= Capacity)
                throw new ArgumentOutOfRangeException(nameof(newCapacity), "New capacity must be larger than current.");

            var ns0 = new NativeArray<T0>(newCapacity, _allocator, NativeArrayOptions.UninitializedMemory);
            var ns1 = new NativeArray<T1>(newCapacity, _allocator, NativeArrayOptions.UninitializedMemory);
            var ns2 = new NativeArray<T2>(newCapacity, _allocator, NativeArrayOptions.UninitializedMemory);
            var ns3 = new NativeArray<T3>(newCapacity, _allocator, NativeArrayOptions.UninitializedMemory);
            if (_activeCount > 0)
            {
                NativeArray<T0>.Copy(_stream0, 0, ns0, 0, _activeCount);
                NativeArray<T1>.Copy(_stream1, 0, ns1, 0, _activeCount);
                NativeArray<T2>.Copy(_stream2, 0, ns2, 0, _activeCount);
                NativeArray<T3>.Copy(_stream3, 0, ns3, 0, _activeCount);
            }
            _stream0.Dispose(); _stream1.Dispose(); _stream2.Dispose(); _stream3.Dispose();
            _stream0 = ns0; _stream1 = ns1; _stream2 = ns2; _stream3 = ns3;
            _indexMap.Resize(newCapacity, _activeCount, ref _freeCount);
        }

        public void Clear()
        {
            _indexMap.Clear(ref _activeCount, ref _freeCount);
        }

        public void Dispose()
        {
            if (_stream0.IsCreated) _stream0.Dispose();
            if (_stream1.IsCreated) _stream1.Dispose();
            if (_stream2.IsCreated) _stream2.Dispose();
            if (_stream3.IsCreated) _stream3.Dispose();
            _indexMap.Dispose();
            _activeCount = 0;
            _freeCount = 0;
        }
    }
}
#endif
