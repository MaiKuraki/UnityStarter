using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Runtime.Interaction
{
    /// <summary>
    /// Data-Oriented spatial hash grid for O(1) neighbor queries at scale (10k+ objects).
    ///
    /// Internal layout uses Structure-of-Arrays (SoA) for cache-friendly iteration:
    /// - Parallel flat arrays for positions, cell hashes, and cell-chain pointers.
    /// - Intrusive doubly-linked list per cell via _nextInCell/_prevInCell arrays (zero List allocations).
    /// - Slot recycling via free-list stack (zero GC on Insert/Remove after warm-up).
    ///
    /// The public API is kept compatible with the original OOP version.
    /// </summary>
    public sealed class SpatialHashGrid : IDisposable
    {
        private const int NullSlot = -1;

        private readonly float _cellSize;
        private readonly float _inverseCellSize;

        // SoA arrays are indexed by slot to keep query traversal cache-friendly.
        private int _slotCapacity;
        private IInteractable[] _items;
        private float[] _posX;
        private float[] _posY;
        private float[] _posZ;
        private long[] _hashes;
        private int[] _nextInCell;
        private int[] _prevInCell;

        private readonly Dictionary<IInteractable, int> _slotLookup;
        private readonly Dictionary<long, int> _cellHeads;

        private int _activeCount;
        private int _slotHighWaterMark;
        private readonly Stack<int> _freeSlots;

        private readonly List<IInteractable> _queryBuffer;

        private readonly ReaderWriterLockSlim _rwLock;

        /// <summary>Number of currently registered interactables.</summary>
        public int ItemCount => _activeCount;

        /// <summary>Number of occupied grid cells.</summary>
        public int CellCount => _cellHeads.Count;

        /// <summary>Current slot array capacity.</summary>
        public int SlotCapacity => _slotCapacity;

        public SpatialHashGrid(float cellSize = 10f, int initialCapacity = 256)
        {
            _cellSize = cellSize;
            _inverseCellSize = 1f / cellSize;

            _slotCapacity = initialCapacity;
            _items = new IInteractable[initialCapacity];
            _posX = new float[initialCapacity];
            _posY = new float[initialCapacity];
            _posZ = new float[initialCapacity];
            _hashes = new long[initialCapacity];
            _nextInCell = new int[initialCapacity];
            _prevInCell = new int[initialCapacity];

            _slotLookup = new Dictionary<IInteractable, int>(initialCapacity);
            _cellHeads = new Dictionary<long, int>(initialCapacity);
            _freeSlots = new Stack<int>(64);
            _queryBuffer = new List<IInteractable>(128);
            _rwLock = new ReaderWriterLockSlim();
            _activeCount = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long HashPosition(float x, float z)
        {
            int cx = (int)Math.Floor(x * _inverseCellSize);
            int cz = (int)Math.Floor(z * _inverseCellSize);
            return ((long)cx << 32) | (uint)cz;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long HashPosition2D(float x, float y)
        {
            int cx = (int)Math.Floor(x * _inverseCellSize);
            int cy = (int)Math.Floor(y * _inverseCellSize);
            return ((long)cx << 32) | (uint)cy;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int AllocateSlot()
        {
            if (_freeSlots.Count > 0)
                return _freeSlots.Pop();

            int slot = _slotHighWaterMark++;
            if (slot >= _slotCapacity)
                GrowArrays(_slotCapacity * 2);

            return slot;
        }

        private void GrowArrays(int newCapacity)
        {
            Array.Resize(ref _items, newCapacity);
            Array.Resize(ref _posX, newCapacity);
            Array.Resize(ref _posY, newCapacity);
            Array.Resize(ref _posZ, newCapacity);
            Array.Resize(ref _hashes, newCapacity);
            Array.Resize(ref _nextInCell, newCapacity);
            Array.Resize(ref _prevInCell, newCapacity);
            _slotCapacity = newCapacity;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void FreeSlot(int slot)
        {
            _items[slot] = null;
            _freeSlots.Push(slot);
        }

        /// <summary>Prepend a slot to the head of the cell chain. O(1).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LinkToCell(int slot, long cellHash)
        {
            _prevInCell[slot] = NullSlot;

            if (_cellHeads.TryGetValue(cellHash, out int oldHead))
            {
                _nextInCell[slot] = oldHead;
                _prevInCell[oldHead] = slot;
            }
            else
            {
                _nextInCell[slot] = NullSlot;
            }

            _cellHeads[cellHash] = slot;
        }

        /// <summary>Remove a slot from its cell chain. O(1) with doubly-linked list.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void UnlinkFromCell(int slot, long cellHash)
        {
            int prev = _prevInCell[slot];
            int next = _nextInCell[slot];

            if (prev != NullSlot)
                _nextInCell[prev] = next;
            else
                // The removed slot was the head, so the cell head must move or be removed.
                if (next != NullSlot)
                    _cellHeads[cellHash] = next;
                else
                    _cellHeads.Remove(cellHash);

            if (next != NullSlot)
                _prevInCell[next] = prev;
        }

        public void Insert(IInteractable item, bool is2D = false)
        {
            Vector3 pos = item.Position;
            long hash = is2D ? HashPosition2D(pos.x, pos.y) : HashPosition(pos.x, pos.z);

            _rwLock.EnterWriteLock();
            try
            {
                if (_slotLookup.ContainsKey(item)) return;

                int slot = AllocateSlot();

                _items[slot] = item;
                _posX[slot] = pos.x;
                _posY[slot] = pos.y;
                _posZ[slot] = pos.z;
                _hashes[slot] = hash;

                LinkToCell(slot, hash);
                _slotLookup[item] = slot;
                _activeCount++;
            }
            finally
            {
                _rwLock.ExitWriteLock();
            }
        }

        public void Remove(IInteractable item)
        {
            _rwLock.EnterWriteLock();
            try
            {
                if (!_slotLookup.TryGetValue(item, out int slot)) return;

                UnlinkFromCell(slot, _hashes[slot]);
                _slotLookup.Remove(item);
                FreeSlot(slot);
                _activeCount--;
            }
            finally
            {
                _rwLock.ExitWriteLock();
            }
        }

        public void UpdatePosition(IInteractable item, bool is2D = false)
        {
            Vector3 pos = item.Position;
            long newHash = is2D ? HashPosition2D(pos.x, pos.y) : HashPosition(pos.x, pos.z);

            _rwLock.EnterWriteLock();
            try
            {
                if (!_slotLookup.TryGetValue(item, out int slot)) return;

                _posX[slot] = pos.x;
                _posY[slot] = pos.y;
                _posZ[slot] = pos.z;

                long oldHash = _hashes[slot];
                if (oldHash == newHash) return;

                UnlinkFromCell(slot, oldHash);
                _hashes[slot] = newHash;
                LinkToCell(slot, newHash);
            }
            finally
            {
                _rwLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Query all items within radius. Returns an internal buffer; callers must not cache it.
        /// </summary>
        public List<IInteractable> QueryRadius(Vector3 center, float radius, bool is2D = false)
        {
            QueryRadiusNonAlloc(center, radius, _queryBuffer, is2D, int.MaxValue, true);
            return _queryBuffer;
        }

        /// <summary>
        /// Query all items within radius into a caller-owned buffer.
        /// The caller controls buffer capacity and lifetime, which avoids shared mutable results.
        /// </summary>
        public int QueryRadiusNonAlloc(Vector3 center, float radius, List<IInteractable> results, bool is2D = false, int maxResults = int.MaxValue, bool allowBufferGrowth = true)
        {
            if (results == null)
                throw new ArgumentNullException(nameof(results));

            results.Clear();
            int effectiveMaxResults = maxResults;
            if (!allowBufferGrowth && effectiveMaxResults > results.Capacity)
                effectiveMaxResults = results.Capacity;

            if (effectiveMaxResults <= 0)
                return 0;

            int cellRadius = (int)Math.Ceiling(radius * _inverseCellSize);
            float cx = center.x;
            float cy = is2D ? center.y : center.z;
            int cellX = (int)Math.Floor(cx * _inverseCellSize);
            int cellY = (int)Math.Floor(cy * _inverseCellSize);
            float radiusSqr = radius * radius;

            float[] posX = _posX;
            float[] posY = _posY;
            float[] posZ = _posZ;
            int[] nextArr = _nextInCell;
            IInteractable[] items = _items;

            _rwLock.EnterReadLock();
            try
            {
                for (int dx = -cellRadius; dx <= cellRadius; dx++)
                {
                    for (int dy = -cellRadius; dy <= cellRadius; dy++)
                    {
                        long hash = ((long)(cellX + dx) << 32) | (uint)(cellY + dy);
                        if (!_cellHeads.TryGetValue(hash, out int slot)) continue;

                        while (slot != NullSlot)
                        {
                            float dxPos = posX[slot] - cx;
                            float dyPos = is2D
                                ? posY[slot] - cy
                                : posZ[slot] - cy;
                            float distSqr = dxPos * dxPos + dyPos * dyPos;

                            if (distSqr <= radiusSqr)
                            {
                                results.Add(items[slot]);
                                if (results.Count >= effectiveMaxResults)
                                    return results.Count;
                            }

                            slot = nextArr[slot];
                        }
                    }
                }
            }
            finally
            {
                _rwLock.ExitReadLock();
            }

            return results.Count;
        }

        public void Clear()
        {
            _rwLock.EnterWriteLock();
            try
            {
                Array.Clear(_items, 0, _slotCapacity);
                _cellHeads.Clear();
                _slotLookup.Clear();
                _freeSlots.Clear();
                _activeCount = 0;
                _slotHighWaterMark = 0;
            }
            finally
            {
                _rwLock.ExitWriteLock();
            }
        }

        public void Dispose()
        {
            _rwLock.EnterWriteLock();
            try
            {
                Array.Clear(_items, 0, _slotCapacity);
                _cellHeads.Clear();
                _slotLookup.Clear();
                _freeSlots.Clear();
                _queryBuffer.Clear();
                _activeCount = 0;
                _slotHighWaterMark = 0;
            }
            finally
            {
                _rwLock.ExitWriteLock();
            }
            _rwLock.Dispose();
        }
    }
}
