using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using CycloneGames.Spatial.Core.Internal;

namespace CycloneGames.Spatial.Core
{
    /// <summary>
    /// Uniform spatial hash grid over an array-backed slot table.
    /// </summary>
    /// <remarks>
    /// Structure-of-arrays: one flat array per per-element field, indexed by slot, plus an intrusive doubly linked
    /// chain of slot indices per cell. A query walks one bucket per covered cell, then one chain.
    /// </remarks>
    /// <remarks>
    /// <see cref="Capacity"/> bounds elements; <see cref="BucketCapacity"/> bounds addressable cells. The bucket table
    /// rebuilds as cells are added and removed, so it does not degrade under sustained churn.
    /// </remarks>
    /// <remarks>
    /// Single writer, no internal lock. Reads may interleave with writes on the same thread only; a Debug build throws
    /// when a second thread writes. Cross-thread access requires external synchronisation.
    /// </remarks>
    /// <remarks>
    /// Broad phase only: it reports which elements are near a point or inside a box, not which shapes intersect.
    /// Boundary tests are inclusive with a small tolerance so a candidate is never missed; exact geometry belongs to
    /// the caller's narrow phase.
    /// </remarks>
    /// <typeparam name="TItem">
    /// Payload stored per element. The payload array is contiguous for both reference and value types. The grid never
    /// compares or hashes the payload.
    /// </typeparam>
    public sealed class SpatialHashGrid<TItem> : ISpatialMutableIndex<TItem>, IDisposable
    {
        private const int InvalidSlot = -1;
        private const byte SlotFree = 0;
        private const byte SlotUsed = 1;

        private const int MinimumCapacity = 64;
        private const int MaximumCapacity = 1 << 24;

        /// <summary>Relative tolerance applied to the squared radius so a boundary candidate is never missed.</summary>
        private const float DistanceTolerance = 1e-5f;

        /// <summary>Absolute tolerance applied to box bounds so a boundary candidate is never missed.</summary>
        private const float BoxTolerance = 1e-4f;

        private readonly int _initialCapacity;
        private readonly SpatialCapacityPolicy _capacityPolicy;

        private GridSettings _settings;

        private int _capacity;
        private int _highWater;
        private int _count;
        private int _peakCount;
        private int _growthCount;
        private long _totalInserted;
        private long _totalRemoved;
        private long _generationWrapCount;

        private TItem[] _items;
        private int[] _next;
        private int[] _prev;
        private CellCoordinate[] _cellKey;
        private float[] _positions;
        private ulong[] _generation;
        private uint[] _layers;
        private byte[] _slotUsed;
        private int[] _freeStack;
        private int _freeCount;

        private BucketTable _buckets;

        private ThreadGuard _threadGuard;
#if DEBUG
        private byte[] _validateScratch;
#endif
        private bool _disposed;

        /// <summary>
        /// Creates a grid.
        /// </summary>
        /// <param name="settings">Cell size and world origin.</param>
        /// <param name="initialCapacity">Slots allocated up front. Values below 64 are raised to 64.</param>
        /// <param name="capacityPolicy">Behaviour when an insert arrives with every slot occupied.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="initialCapacity"/> is negative.</exception>
        public SpatialHashGrid(
            GridSettings settings,
            int initialCapacity = MinimumCapacity,
            SpatialCapacityPolicy capacityPolicy = SpatialCapacityPolicy.Grow)
        {
            if (initialCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialCapacity), "Initial capacity cannot be negative.");
            }

            _settings = settings;
            _capacityPolicy = capacityPolicy;
            _initialCapacity = NormalizeCapacity(initialCapacity);
            _capacity = _initialCapacity;
            _buckets = new BucketTable(16);

            _items = new TItem[_capacity];
            _next = new int[_capacity];
            _prev = new int[_capacity];
            _cellKey = new CellCoordinate[_capacity];
            _positions = new float[_capacity * 3];
            _generation = new ulong[_capacity];
            _layers = new uint[_capacity];
            _slotUsed = new byte[_capacity];
            _freeStack = new int[_capacity];
        }

        /// <summary>Convenience overload that builds settings from a cell size at the world origin.</summary>
        public SpatialHashGrid(
            float cellSize,
            int initialCapacity = MinimumCapacity,
            SpatialCapacityPolicy capacityPolicy = SpatialCapacityPolicy.Grow)
            : this(new GridSettings(cellSize), initialCapacity, capacityPolicy)
        {
        }

        /// <summary>Cell size and origin currently in use.</summary>
        public GridSettings Settings
        {
            get { return _settings; }
        }

        /// <summary>Edge length of one cell in world units.</summary>
        public float CellSize
        {
            get { return _settings.CellSize; }
        }

        /// <summary>Elements currently stored.</summary>
        public int Count
        {
            get { return _count; }
        }

        /// <summary>Elements that fit without growing.</summary>
        public int Capacity
        {
            get { return _capacity; }
        }

        /// <summary>Buckets currently allocated in the open-addressing cell table.</summary>
        public int BucketCapacity
        {
            get { return _buckets == null ? 0 : _buckets.Capacity; }
        }

        /// <summary>True after <see cref="Dispose"/>.</summary>
        public bool IsDisposed
        {
            get { return _disposed; }
        }

        /// <summary>
        /// Adds an element.
        /// </summary>
        /// <remarks>
        /// <para>Complexity: amortized O(1); O(n) when the slot arrays grow or the bucket table rebuilds.</para>
        /// <para>
        /// Duplicate payloads are stored as independent elements with independent handles. The grid does not track
        /// identity.
        /// </para>
        /// </remarks>
        /// <returns>
        /// <c>false</c> when the position is not finite, the index is disposed, or the index is full under
        /// <see cref="SpatialCapacityPolicy.Fixed"/>.
        /// </returns>
        public bool TryInsert(TItem item, float x, float y, float z, out SpatialHandle handle)
        {
            return TryInsert(item, x, y, z, SpatialLayers.Default, out handle);
        }

        /// <summary>
        /// Adds an element with explicit layer bits. Zero is normalised to <see cref="SpatialLayers.Default"/>, because
        /// a zero layer would match no query at all.
        /// </summary>
        public bool TryInsert(TItem item, float x, float y, float z, uint layerMask, out SpatialHandle handle)
        {
            _threadGuard.AssertWriteThread();

            if (_disposed || !IsFinitePosition(x, y, z))
            {
                handle = SpatialHandle.Invalid;
                return false;
            }

            // Resolve capacity before creating a bucket: a rejected insert must leave the grid unchanged.
            if (_freeCount == 0 && _highWater >= _capacity)
            {
                if (_capacityPolicy == SpatialCapacityPolicy.Fixed)
                {
                    handle = SpatialHandle.Invalid;
                    return false;
                }

                if (!GrowSlotArrays(_capacity * 2))
                {
                    handle = SpatialHandle.Invalid;
                    return false;
                }
            }

            CellCoordinate cell = _settings.WorldToCell(x, y, z);
            int bucket = _buckets.FindOrInsert(cell);

            int slot;
            if (_freeCount > 0)
            {
                slot = _freeStack[--_freeCount];
            }
            else
            {
                slot = _highWater;
                _highWater++;
            }

            _items[slot] = item;
            _layers[slot] = layerMask == 0u ? SpatialLayers.Default : layerMask;
            _cellKey[slot] = cell;
            int offset = slot * 3;
            _positions[offset] = x;
            _positions[offset + 1] = y;
            _positions[offset + 2] = z;
            _slotUsed[slot] = SlotUsed;
            _generation[slot] = AdvanceGeneration(_generation[slot]);
            LinkAtBucketHead(bucket, slot);

            _count++;
            if (_count > _peakCount)
            {
                _peakCount = _count;
            }

            _totalInserted++;
            handle = new SpatialHandle(slot, _generation[slot]);
            return true;
        }

        /// <summary>
        /// Moves an element.
        /// </summary>
        /// <remarks>Complexity: O(1) when the element stays in its cell, O(k) across cells with k the target chain length.</remarks>
        /// <returns><c>false</c> when the handle is stale, the position is not finite, or the index is disposed.</returns>
        public bool TryUpdate(SpatialHandle handle, float x, float y, float z)
        {
            _threadGuard.AssertWriteThread();

            if (!TryResolve(handle, out int slot) || !IsFinitePosition(x, y, z))
            {
                return false;
            }

            int offset = slot * 3;
            _positions[offset] = x;
            _positions[offset + 1] = y;
            _positions[offset + 2] = z;

            CellCoordinate newCell = _settings.WorldToCell(x, y, z);
            if (newCell == _cellKey[slot])
            {
                return true;
            }

            DetachFromCell(slot);

            int bucket = _buckets.FindOrInsert(newCell);
            _cellKey[slot] = newCell;
            LinkAtBucketHead(bucket, slot);
            return true;
        }

        /// <summary>
        /// Removes an element and recycles its slot.
        /// </summary>
        /// <remarks>Complexity: O(1). The slot generation advances so the removed handle can never match again.</remarks>
        /// <returns><c>false</c> when the handle is stale or the index is disposed.</returns>
        public bool TryRemove(SpatialHandle handle)
        {
            _threadGuard.AssertWriteThread();

            if (!TryResolve(handle, out int slot))
            {
                return false;
            }

            DetachFromCell(slot);
            _items[slot] = default;
            _slotUsed[slot] = SlotFree;
            _freeStack[_freeCount++] = slot;
            _count--;
            _totalRemoved++;
            return true;
        }

        /// <summary>
        /// Reads the position stored for a live element.
        /// </summary>
        /// <remarks>Complexity: O(1). Useful for verification and for callers that keep only the handle.</remarks>
        public bool TryGetPosition(SpatialHandle handle, out float x, out float y, out float z)
        {
            if (!TryResolve(handle, out int slot))
            {
                x = 0f;
                y = 0f;
                z = 0f;
                return false;
            }

            int offset = slot * 3;
            x = _positions[offset];
            y = _positions[offset + 1];
            z = _positions[offset + 2];
            return true;
        }

        /// <summary>
        /// Reads the layer bits of a live element.
        /// </summary>
        /// <remarks>Complexity: O(1).</remarks>
        public bool TryGetLayer(SpatialHandle handle, out uint layerMask)
        {
            if (!TryResolve(handle, out int slot))
            {
                layerMask = 0u;
                return false;
            }

            layerMask = _layers[slot];
            return true;
        }

        /// <summary>
        /// Reassigns the layer bits of a live element.
        /// </summary>
        /// <remarks>
        /// Complexity: O(1). Zero is rejected rather than normalised, because silently turning an element invisible to
        /// every query is worse than a <c>false</c>.
        /// </remarks>
        /// <returns><c>false</c> when the handle is stale, the index is disposed, or <paramref name="layerMask"/> is zero.</returns>
        public bool TrySetLayer(SpatialHandle handle, uint layerMask)
        {
            _threadGuard.AssertWriteThread();

            if (layerMask == 0u || !TryResolve(handle, out int slot))
            {
                return false;
            }

            _layers[slot] = layerMask;
            return true;
        }

        /// <summary>
        /// Removes every element while keeping the allocated capacity.
        /// </summary>
        /// <remarks>
        /// Complexity: O(occupied slots). Every live generation advances, so a handle issued before the call can never
        /// match an element inserted after it, even though the same slot indices are reused.
        /// </remarks>
        public void Clear()
        {
            _threadGuard.AssertWriteThread();

            if (_disposed)
            {
                return;
            }

            _freeCount = 0;
            for (int slot = 0; slot < _highWater; slot++)
            {
                if (_slotUsed[slot] == SlotUsed)
                {
                    _generation[slot] = AdvanceGeneration(_generation[slot]);
                    _items[slot] = default;
                    _layers[slot] = 0u;
                    _slotUsed[slot] = SlotFree;
                }
            }

            // Reset the high-water mark so TrimExcess can release the arrays. Generations are kept, so handles issued
            // before the call still cannot match a slot reused afterwards.
            _highWater = 0;
            _count = 0;
            _buckets.Clear();
        }

        /// <summary>
        /// Reserves room for <paramref name="capacity"/> elements and for that many distinct cells.
        /// </summary>
        /// <remarks>
        /// Complexity: O(n) for the slot copies plus O(bucket capacity) for the table. Call it during a loading screen
        /// or a respawn, never inside a per-frame path, and the corresponding growth counters stay flat.
        /// </remarks>
        public void EnsureCapacity(int capacity)
        {
            _threadGuard.AssertWriteThread();

            if (_disposed || capacity <= 0)
            {
                return;
            }

            if (capacity > MaximumCapacity)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity exceeds the supported maximum.");
            }

            int target = _capacity;
            while (target < capacity)
            {
                target *= 2;
            }

            if (target > _capacity)
            {
                GrowSlotArrays(target);
            }

            _buckets.EnsureBucketCount(capacity);
        }

        /// <summary>
        /// Releases slot arrays that the current element count no longer needs.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Complexity: O(n). Shrinking never relocates an element, so every live handle stays valid. That also bounds
        /// what can be released: a slot index that is still reachable keeps its slot, which means the array cannot
        /// shrink below the highest slot ever used until <see cref="Clear"/> resets the high-water mark.
        /// </para>
        /// <para>This is the only place capacity decreases. No removal path shrinks anything on its own.</para>
        /// </remarks>
        /// <returns><c>true</c> when the arrays actually shrank.</returns>
        public bool TrimExcess()
        {
            _threadGuard.AssertWriteThread();

            if (_disposed)
            {
                return false;
            }

            int target = _highWater < _initialCapacity ? _initialCapacity : NextPowerOfTwo(_highWater);
            if (target >= _capacity)
            {
                return false;
            }

            ShrinkSlotArrays(target);
            return true;
        }

        /// <summary>
        /// Rebases the grid origin and re-buckets every element.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Complexity: O(n). Slots and generations are untouched, so live handles stay valid and
        /// <see cref="TryGetPosition"/> keeps working; only the cell assignment changes.
        /// </para>
        /// <para>Call it when positions drift far enough from the origin to lose float resolution, not per frame.</para>
        /// </remarks>
        /// <exception cref="ArgumentOutOfRangeException">The origin is not finite.</exception>
        public void SetOrigin(float originX, float originY, float originZ)
        {
            _threadGuard.AssertWriteThread();

            if (_disposed)
            {
                return;
            }

            GridSettings updated = _settings.WithOrigin(originX, originY, originZ);
            _settings = updated;

            if (_count == 0)
            {
                return;
            }

            _buckets.Clear();
            for (int slot = 0; slot < _highWater; slot++)
            {
                if (_slotUsed[slot] != SlotUsed)
                {
                    continue;
                }

                _next[slot] = InvalidSlot;
                _prev[slot] = InvalidSlot;
            }

            for (int slot = 0; slot < _highWater; slot++)
            {
                if (_slotUsed[slot] != SlotUsed)
                {
                    continue;
                }

                int offset = slot * 3;
                CellCoordinate cell = _settings.WorldToCell(
                    _positions[offset],
                    _positions[offset + 1],
                    _positions[offset + 2]);
                _cellKey[slot] = cell;
                int bucket = _buckets.FindOrInsert(cell);
                LinkAtBucketHead(bucket, slot);
            }
        }

        /// <summary>
        /// Writes the elements inside a sphere into a caller-owned array.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Results are unordered and truncated. The method writes at most
        /// <c>min(maxResults, results.Length)</c> entries and returns early once that limit is reached, so a truncated
        /// query is also a cheap one.
        /// </para>
        /// <para>
        /// Complexity: O(m + k), with m covered cells and k candidates. Falls back to O(n) when the covered cell range
        /// exceeds <see cref="SpatialQueryLimits.MaxCellVisitsPerQuery"/>.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="results"/> is null.</exception>
        public int QuerySphere(float centerX, float centerY, float centerZ, float radius, TItem[] results, int maxResults, uint layerMask = SpatialLayers.All)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            if (_disposed || maxResults <= 0 || _count == 0 || results.Length == 0)
            {
                return 0;
            }

            if (!IsFinitePosition(centerX, centerY, centerZ) || !GridSettings.IsFinite(radius) || radius < 0f)
            {
                return 0;
            }

            int limit = maxResults < results.Length ? maxResults : results.Length;
            float radiusSquared = radius * radius;
            radiusSquared += (radiusSquared * DistanceTolerance) + DistanceTolerance;

            CellCoordinate minCell;
            CellCoordinate maxCell;
            if (!_settings.CellRangeForSphere(centerX, centerY, centerZ, radius, out minCell, out maxCell))
            {
                return 0;
            }

            if (!CellRangeBudget.IsWithin(minCell, maxCell))
            {
                return QuerySphereLinear(centerX, centerY, centerZ, radiusSquared, layerMask, results, limit);
            }

            int written = 0;
            var walker = new CellWalker(this, minCell, maxCell);
            while (walker.MoveNext())
            {
                int slot = walker.Current;
                if ((_layers[slot] & layerMask) == 0u)
                {
                    continue;
                }
                int offset = slot * 3;
                float dx = _positions[offset] - centerX;
                float dy = _positions[offset + 1] - centerY;
                float dz = _positions[offset + 2] - centerZ;
                if ((dx * dx) + (dy * dy) + (dz * dz) > radiusSquared)
                {
                    continue;
                }

                results[written] = _items[slot];
                written++;
                if (written >= limit)
                {
                    return written;
                }
            }

            return written;
        }

        /// <summary>
        /// Writes the elements inside a sphere into a caller-owned list.
        /// </summary>
        /// <remarks>
        /// <para>
        /// The list is cleared first. Filling stops at <c>min(maxResults, results.Capacity)</c> so the list never
        /// reallocates; pass a list with enough capacity if you expect a large result.
        /// </para>
        /// <para>Complexity: same as the array overload.</para>
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="results"/> is null.</exception>
        public int QuerySphere(float centerX, float centerY, float centerZ, float radius, List<TItem> results, int maxResults = int.MaxValue, uint layerMask = SpatialLayers.All)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            results.Clear();

            if (_disposed || maxResults <= 0 || _count == 0 || results.Capacity == 0)
            {
                return 0;
            }

            if (!IsFinitePosition(centerX, centerY, centerZ) || !GridSettings.IsFinite(radius) || radius < 0f)
            {
                return 0;
            }

            int limit = maxResults < results.Capacity ? maxResults : results.Capacity;
            float radiusSquared = radius * radius;
            radiusSquared += (radiusSquared * DistanceTolerance) + DistanceTolerance;

            CellCoordinate minCell;
            CellCoordinate maxCell;
            if (!_settings.CellRangeForSphere(centerX, centerY, centerZ, radius, out minCell, out maxCell))
            {
                return 0;
            }

            if (!CellRangeBudget.IsWithin(minCell, maxCell))
            {
                return QuerySphereLinearToList(centerX, centerY, centerZ, radiusSquared, layerMask, results, limit);
            }

            var walker = new CellWalker(this, minCell, maxCell);
            while (walker.MoveNext())
            {
                int slot = walker.Current;
                if ((_layers[slot] & layerMask) == 0u)
                {
                    continue;
                }
                int offset = slot * 3;
                float dx = _positions[offset] - centerX;
                float dy = _positions[offset + 1] - centerY;
                float dz = _positions[offset + 2] - centerZ;
                if ((dx * dx) + (dy * dy) + (dz * dz) > radiusSquared)
                {
                    continue;
                }

                results.Add(_items[slot]);
                if (results.Count >= limit)
                {
                    break;
                }
            }

            return results.Count;
        }

        /// <summary>
        /// Writes the elements inside an axis-aligned box into a caller-owned array.
        /// </summary>
        /// <remarks>
        /// Inclusive on every face with a small tolerance, so an element sitting exactly on a face is returned.
        /// Results are unordered and truncated. Complexity: O(m + k).
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="results"/> is null.</exception>
        public int QueryBox(
            float minX,
            float minY,
            float minZ,
            float maxX,
            float maxY,
            float maxZ,
            TItem[] results,
            int maxResults,
            uint layerMask = SpatialLayers.All)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            if (_disposed || maxResults <= 0 || _count == 0 || results.Length == 0)
            {
                return 0;
            }

            CellCoordinate minCell;
            CellCoordinate maxCell;
            if (!_settings.CellRangeForAabb(minX, minY, minZ, maxX, maxY, maxZ, out minCell, out maxCell))
            {
                return 0;
            }

            int limit = maxResults < results.Length ? maxResults : results.Length;
            if (!CellRangeBudget.IsWithin(minCell, maxCell))
            {
                return QueryBoxLinear(minX, minY, minZ, maxX, maxY, maxZ, layerMask, results, limit);
            }

            int written = 0;
            var walker = new CellWalker(this, minCell, maxCell);
            while (walker.MoveNext())
            {
                int slot = walker.Current;
                if ((_layers[slot] & layerMask) == 0u)
                {
                    continue;
                }
                if (!IsInsideBox(slot, minX, minY, minZ, maxX, maxY, maxZ))
                {
                    continue;
                }

                results[written] = _items[slot];
                written++;
                if (written >= limit)
                {
                    return written;
                }
            }

            return written;
        }

        /// <summary>
        /// Writes the elements inside an axis-aligned box into a caller-owned list.
        /// </summary>
        /// <remarks>The list is cleared first and never grows. Complexity: O(m + k).</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="results"/> is null.</exception>
        public int QueryBox(
            float minX,
            float minY,
            float minZ,
            float maxX,
            float maxY,
            float maxZ,
            List<TItem> results,
            int maxResults = int.MaxValue,
            uint layerMask = SpatialLayers.All)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            results.Clear();

            if (_disposed || maxResults <= 0 || _count == 0 || results.Capacity == 0)
            {
                return 0;
            }

            CellCoordinate minCell;
            CellCoordinate maxCell;
            if (!_settings.CellRangeForAabb(minX, minY, minZ, maxX, maxY, maxZ, out minCell, out maxCell))
            {
                return 0;
            }

            int limit = maxResults < results.Capacity ? maxResults : results.Capacity;
            if (!CellRangeBudget.IsWithin(minCell, maxCell))
            {
                return QueryBoxLinearToList(minX, minY, minZ, maxX, maxY, maxZ, layerMask, results, limit);
            }

            var walker = new CellWalker(this, minCell, maxCell);
            while (walker.MoveNext())
            {
                int slot = walker.Current;
                if ((_layers[slot] & layerMask) == 0u)
                {
                    continue;
                }
                if (!IsInsideBox(slot, minX, minY, minZ, maxX, maxY, maxZ))
                {
                    continue;
                }

                results.Add(_items[slot]);
                if (results.Count >= limit)
                {
                    break;
                }
            }

            return results.Count;
        }

        /// <summary>
        /// Writes the closest elements inside a sphere into two parallel caller-owned buffers, ordered by distance.
        /// </summary>
        /// <remarks>
        /// <para>
        /// <c>results[i]</c> is at <c>squaredDistances[i]</c>, ascending. Squared distance avoids <c>sqrt</c> and gives
        /// the same ordering.
        /// </para>
        /// <para>
        /// Unlike a truncated radius query followed by a caller-side sort, this keeps the nearest candidates regardless
        /// of traversal order.
        /// </para>
        /// <para>
        /// Complexity: O(m + k) plus O(maxResults) per accepted candidate. The buffer is insertion sorted; keep
        /// <paramref name="maxResults"/> in the tens.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Either output buffer is null.</exception>
        public int QueryNearest(
            float centerX,
            float centerY,
            float centerZ,
            float radius,
            int maxResults,
            uint layerMask,
            TItem[] results,
            float[] squaredDistances)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            if (squaredDistances == null)
            {
                throw new ArgumentNullException(nameof(squaredDistances));
            }

            if (_disposed || maxResults <= 0 || _count == 0)
            {
                return 0;
            }

            if (!IsFinitePosition(centerX, centerY, centerZ) || !GridSettings.IsFinite(radius) || radius < 0f)
            {
                return 0;
            }

            int limit = maxResults;
            if (results.Length < limit)
            {
                limit = results.Length;
            }

            if (squaredDistances.Length < limit)
            {
                limit = squaredDistances.Length;
            }

            if (limit <= 0)
            {
                return 0;
            }

            float radiusSquared = radius * radius;
            radiusSquared += (radiusSquared * DistanceTolerance) + DistanceTolerance;

            CellCoordinate minCell;
            CellCoordinate maxCell;
            if (!_settings.CellRangeForSphere(centerX, centerY, centerZ, radius, out minCell, out maxCell))
            {
                return 0;
            }

            if (!CellRangeBudget.IsWithin(minCell, maxCell))
            {
                return QueryNearestLinear(
                    centerX,
                    centerY,
                    centerZ,
                    radiusSquared,
                    layerMask,
                    limit,
                    results,
                    squaredDistances);
            }

            int count = 0;
            var walker = new CellWalker(this, minCell, maxCell);
            while (walker.MoveNext())
            {
                int slot = walker.Current;
                if ((_layers[slot] & layerMask) == 0u)
                {
                    continue;
                }

                int offset = slot * 3;
                float dx = _positions[offset] - centerX;
                float dy = _positions[offset + 1] - centerY;
                float dz = _positions[offset + 2] - centerZ;
                float distanceSquared = (dx * dx) + (dy * dy) + (dz * dz);
                if (distanceSquared > radiusSquared)
                {
                    continue;
                }

                count = NearestBuffer.Insert(count, limit, distanceSquared, _items[slot], results, squaredDistances);
            }

            return count;
        }

        /// <summary>
        /// Captures the current counters without allocating.
        /// </summary>
        public SpatialStatistics GetStatistics()
        {
            return new SpatialStatistics(
                _count,
                _buckets == null ? 0 : _buckets.OccupiedCount,
                _capacity,
                _buckets == null ? 0 : _buckets.Capacity,
                _peakCount,
                _growthCount,
                _buckets == null ? 0 : _buckets.RebuildCount,
                _totalInserted,
                _totalRemoved,
                _generationWrapCount);
        }

        /// <summary>
        /// Re-captures the writing thread after the index has been handed to another thread.
        /// </summary>
        /// <remarks>
        /// Debug-only; compiles to nothing in a Release build. Call it once after ownership transfers, before the new
        /// thread writes. It is a diagnostic escape hatch, not a synchronisation primitive: the caller still has to
        /// guarantee that no write is in flight on the old thread.
        /// </remarks>
        public void RebindWriteThread()
        {
#if DEBUG
            _threadGuard = default;
#endif
        }

        /// <summary>
        /// Verifies every structural invariant.
        /// </summary>
        /// <remarks>
        /// <para>Debug-only: returns <c>true</c> unchecked in a Release build.</para>
        /// <para>Allocates a byte-per-slot marker array on the first call.</para>
        /// <para>Checked invariants:</para>
        /// <list type="number">
        /// <item><description>Every occupied bucket's chain terminates, is properly back-linked and ends at -1.</description></item>
        /// <item><description>Every slot appears in exactly one chain.</description></item>
        /// <item><description>Every chain slot's cell key equals its bucket's key, and the slot is marked used.</description></item>
        /// <item><description>Reachable slots equal <see cref="Count"/>.</description></item>
        /// <item><description>The free stack holds exactly the unused slots, with no duplicates.</description></item>
        /// <item><description>Every used slot has a non-zero generation.</description></item>
        /// </list>
        /// </remarks>
        public bool ValidateStructure()
        {
#if DEBUG
            if (_disposed)
            {
                return false;
            }

            if (_validateScratch == null || _validateScratch.Length < _capacity)
            {
                _validateScratch = new byte[_capacity];
            }
            else
            {
                Array.Clear(_validateScratch, 0, _capacity);
            }

            int reachable = 0;
            int bucketCapacity = _buckets.Capacity;
            for (int bucket = 0; bucket < bucketCapacity; bucket++)
            {
                if (!_buckets.IsOccupied(bucket))
                {
                    continue;
                }

                CellCoordinate key = _buckets.KeyAt(bucket);
                int slot = _buckets.HeadAt(bucket);
                if (slot == InvalidSlot)
                {
                    return false;
                }

                int previous = InvalidSlot;
                int guard = 0;
                while (slot != InvalidSlot)
                {
                    if (slot < 0 || slot >= _highWater)
                    {
                        return false;
                    }

                    if (_slotUsed[slot] != SlotUsed)
                    {
                        return false;
                    }

                    if (_validateScratch[slot] != 0)
                    {
                        return false;
                    }

                    if (_prev[slot] != previous)
                    {
                        return false;
                    }

                    if (_cellKey[slot] != key)
                    {
                        return false;
                    }

                    if (_generation[slot] == 0UL)
                    {
                        return false;
                    }

                    if (_layers[slot] == 0u)
                    {
                        return false;
                    }

                    _validateScratch[slot] = 1;
                    reachable++;
                    previous = slot;
                    slot = _next[slot];

                    guard++;
                    if (guard > _capacity)
                    {
                        return false;
                    }
                }
            }

            if (reachable != _count)
            {
                return false;
            }

            if (_freeCount != _highWater - _count)
            {
                return false;
            }

            int freeSeen = 0;
            for (int i = 0; i < _freeCount; i++)
            {
                int slot = _freeStack[i];
                if (slot < 0 || slot >= _highWater)
                {
                    return false;
                }

                if (_slotUsed[slot] != SlotFree)
                {
                    return false;
                }

                freeSeen++;
            }

            if (freeSeen + reachable != _highWater)
            {
                return false;
            }

            for (int slot = 0; slot < _highWater; slot++)
            {
                bool reachableSlot = _validateScratch[slot] != 0;
                bool inUse = _slotUsed[slot] == SlotUsed;
                if (reachableSlot != inUse)
                {
                    return false;
                }
            }

            return true;
#else
            return true;
#endif
        }

#if DEBUG
        /// <summary>
        /// Breaks one structural invariant on purpose, so a test can prove <see cref="ValidateStructure"/> detects it.
        /// </summary>
        /// <remarks>
        /// Debug builds only; absent from a Release assembly. <c>internal</c>, reachable by the test assembly through
        /// <c>InternalsVisibleTo</c>. The damage is real: do not keep using the index afterwards.
        /// </remarks>
        /// <returns><c>true</c> when the requested corruption was applicable and applied.</returns>
        internal bool CorruptForTest(SpatialCorruptionKind kind)
        {
            if (_disposed || _count == 0)
            {
                return false;
            }

            int victim = InvalidSlot;
            for (int slot = 0; slot < _highWater; slot++)
            {
                if (_slotUsed[slot] == SlotUsed)
                {
                    victim = slot;
                    break;
                }
            }

            if (victim == InvalidSlot)
            {
                return false;
            }

            switch (kind)
            {
                case SpatialCorruptionKind.BreakChainLink:
                    _next[victim] = victim;
                    return true;

                case SpatialCorruptionKind.MismatchCellKey:
                    _cellKey[victim] = _cellKey[victim].Offset(1, 0, 0);
                    return true;

                case SpatialCorruptionKind.DesynchronizeFreeCount:
                    _freeCount++;
                    return true;

                case SpatialCorruptionKind.ZeroGeneration:
                    _generation[victim] = 0UL;
                    return true;

                case SpatialCorruptionKind.OrphanBucketChain:
                    for (int bucket = 0; bucket < _buckets.Capacity; bucket++)
                    {
                        if (_buckets.IsOccupied(bucket))
                        {
                            _buckets.SetHeadAt(bucket, InvalidSlot);
                            return true;
                        }
                    }

                    return false;

                default:
                    return false;
            }
        }
#endif

        /// <summary>Releases every array reference so the index can be collected.</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _items = null;
            _next = null;
            _prev = null;
            _cellKey = null;
            _positions = null;
            _generation = null;
            _layers = null;
            _slotUsed = null;
            _freeStack = null;
            _buckets = null;
#if DEBUG
            _validateScratch = null;
#endif
            _capacity = 0;
            _highWater = 0;
            _count = 0;
            _freeCount = 0;
        }

        private int QuerySphereLinear(
            float centerX,
            float centerY,
            float centerZ,
            float radiusSquared,
            uint layerMask,
            TItem[] results,
            int limit)
        {
            int written = 0;
            for (int slot = 0; slot < _highWater; slot++)
            {
                if (_slotUsed[slot] != SlotUsed || (_layers[slot] & layerMask) == 0u)
                {
                    continue;
                }

                int offset = slot * 3;
                float dx = _positions[offset] - centerX;
                float dy = _positions[offset + 1] - centerY;
                float dz = _positions[offset + 2] - centerZ;
                if ((dx * dx) + (dy * dy) + (dz * dz) > radiusSquared)
                {
                    continue;
                }

                results[written] = _items[slot];
                written++;
                if (written >= limit)
                {
                    return written;
                }
            }

            return written;
        }

        private int QuerySphereLinearToList(
            float centerX,
            float centerY,
            float centerZ,
            float radiusSquared,
            uint layerMask,
            List<TItem> results,
            int limit)
        {
            for (int slot = 0; slot < _highWater; slot++)
            {
                if (_slotUsed[slot] != SlotUsed || (_layers[slot] & layerMask) == 0u)
                {
                    continue;
                }

                int offset = slot * 3;
                float dx = _positions[offset] - centerX;
                float dy = _positions[offset + 1] - centerY;
                float dz = _positions[offset + 2] - centerZ;
                if ((dx * dx) + (dy * dy) + (dz * dz) > radiusSquared)
                {
                    continue;
                }

                results.Add(_items[slot]);
                if (results.Count >= limit)
                {
                    break;
                }
            }

            return results.Count;
        }

        private int QueryBoxLinear(
            float minX,
            float minY,
            float minZ,
            float maxX,
            float maxY,
            float maxZ,
            uint layerMask,
            TItem[] results,
            int limit)
        {
            int written = 0;
            for (int slot = 0; slot < _highWater; slot++)
            {
                if (_slotUsed[slot] != SlotUsed || (_layers[slot] & layerMask) == 0u)
                {
                    continue;
                }

                if (!IsInsideBox(slot, minX, minY, minZ, maxX, maxY, maxZ))
                {
                    continue;
                }

                results[written] = _items[slot];
                written++;
                if (written >= limit)
                {
                    return written;
                }
            }

            return written;
        }

        private int QueryBoxLinearToList(
            float minX,
            float minY,
            float minZ,
            float maxX,
            float maxY,
            float maxZ,
            uint layerMask,
            List<TItem> results,
            int limit)
        {
            for (int slot = 0; slot < _highWater; slot++)
            {
                if (_slotUsed[slot] != SlotUsed || (_layers[slot] & layerMask) == 0u)
                {
                    continue;
                }

                if (!IsInsideBox(slot, minX, minY, minZ, maxX, maxY, maxZ))
                {
                    continue;
                }

                results.Add(_items[slot]);
                if (results.Count >= limit)
                {
                    break;
                }
            }

            return results.Count;
        }

        private int QueryNearestLinear(
            float centerX,
            float centerY,
            float centerZ,
            float radiusSquared,
            uint layerMask,
            int limit,
            TItem[] results,
            float[] squaredDistances)
        {
            int count = 0;
            for (int slot = 0; slot < _highWater; slot++)
            {
                if (_slotUsed[slot] != SlotUsed || (_layers[slot] & layerMask) == 0u)
                {
                    continue;
                }

                int offset = slot * 3;
                float dx = _positions[offset] - centerX;
                float dy = _positions[offset + 1] - centerY;
                float dz = _positions[offset + 2] - centerZ;
                float distanceSquared = (dx * dx) + (dy * dy) + (dz * dz);
                if (distanceSquared > radiusSquared)
                {
                    continue;
                }

                count = NearestBuffer.Insert(count, limit, distanceSquared, _items[slot], results, squaredDistances);
            }

            return count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsInsideBox(int slot, float minX, float minY, float minZ, float maxX, float maxY, float maxZ)
        {
            int offset = slot * 3;
            float x = _positions[offset];
            float y = _positions[offset + 1];
            float z = _positions[offset + 2];
            return x >= minX - BoxTolerance && x <= maxX + BoxTolerance &&
                   y >= minY - BoxTolerance && y <= maxY + BoxTolerance &&
                   z >= minZ - BoxTolerance && z <= maxZ + BoxTolerance;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryResolve(SpatialHandle handle, out int slot)
        {
            slot = handle.Slot;
            if (_disposed || handle.Generation == 0UL || (uint)slot >= (uint)_highWater)
            {
                return false;
            }

            return _slotUsed[slot] == SlotUsed && _generation[slot] == handle.Generation;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LinkAtBucketHead(int bucket, int slot)
        {
            int head = _buckets.HeadAt(bucket);
            _prev[slot] = InvalidSlot;
            _next[slot] = head;
            if (head != InvalidSlot)
            {
                _prev[head] = slot;
            }

            _buckets.SetHeadAt(bucket, slot);
        }

        private void DetachFromCell(int slot)
        {
            CellCoordinate cell = _cellKey[slot];
            int bucket = _buckets.Find(cell);
            if (bucket < 0)
            {
                return;
            }

            int previous = _prev[slot];
            int next = _next[slot];
            if (previous != InvalidSlot)
            {
                _next[previous] = next;
            }
            else
            {
                _buckets.SetHeadAt(bucket, next);
            }

            if (next != InvalidSlot)
            {
                _prev[next] = previous;
            }

            _next[slot] = InvalidSlot;
            _prev[slot] = InvalidSlot;

            if (_buckets.HeadAt(bucket) == InvalidSlot)
            {
                _buckets.Remove(bucket);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ulong AdvanceGeneration(ulong generation)
        {
            generation++;
            if (generation == 0UL)
            {
                _generationWrapCount++;
                generation = 1UL;
            }

            return generation;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsFinitePosition(float x, float y, float z)
        {
            return GridSettings.IsFinite(x) && GridSettings.IsFinite(y) && GridSettings.IsFinite(z);
        }

        private bool GrowSlotArrays(int newCapacity)
        {
            if (newCapacity > MaximumCapacity)
            {
                return false;
            }

            int target = NormalizeCapacity(newCapacity);
            GrowSlotArraysTo(target);
            return true;
        }

        private void GrowSlotArraysTo(int newCapacity)
        {
            int oldCapacity = _capacity;
            if (newCapacity <= oldCapacity)
            {
                return;
            }

            Array.Resize(ref _items, newCapacity);
            Array.Resize(ref _next, newCapacity);
            Array.Resize(ref _prev, newCapacity);
            Array.Resize(ref _cellKey, newCapacity);
            Array.Resize(ref _generation, newCapacity);
            Array.Resize(ref _layers, newCapacity);
            Array.Resize(ref _slotUsed, newCapacity);
            Array.Resize(ref _freeStack, newCapacity);

            var positions = new float[newCapacity * 3];
            Array.Copy(_positions, positions, oldCapacity * 3);
            _positions = positions;

            _capacity = newCapacity;
            _growthCount++;
        }

        private void ShrinkSlotArrays(int newCapacity)
        {
            int oldCapacity = _capacity;
            if (newCapacity >= oldCapacity || newCapacity < _highWater)
            {
                return;
            }

            Array.Resize(ref _items, newCapacity);
            Array.Resize(ref _next, newCapacity);
            Array.Resize(ref _prev, newCapacity);
            Array.Resize(ref _cellKey, newCapacity);
            Array.Resize(ref _generation, newCapacity);
            Array.Resize(ref _layers, newCapacity);
            Array.Resize(ref _slotUsed, newCapacity);
            Array.Resize(ref _freeStack, newCapacity);

            var positions = new float[newCapacity * 3];
            Array.Copy(_positions, positions, newCapacity * 3);
            _positions = positions;

            _capacity = newCapacity;
        }

        private static int NormalizeCapacity(int capacity)
        {
            if (capacity < MinimumCapacity)
            {
                return MinimumCapacity;
            }

            return NextPowerOfTwo(capacity);
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

        /// <summary>
        /// Iterates the slots of every cell inside an inclusive cell range.
        /// </summary>
        /// <remarks>
        /// Exists because a query has four output shapes (array/list, sphere/box) and duplicating the cell cursor four
        /// times is how the four copies drift apart. Zero allocation; the struct lives on the stack.
        /// </remarks>
        private struct CellWalker
        {
            private readonly SpatialHashGrid<TItem> _grid;
            private readonly CellCoordinate _min;
            private readonly CellCoordinate _max;

            private int _x;
            private int _y;
            private int _z;
            private int _slot;
            private bool _cursorReady;
            private bool _finished;

            public CellWalker(SpatialHashGrid<TItem> grid, CellCoordinate min, CellCoordinate max)
            {
                _grid = grid;
                _min = min;
                _max = max;
                _x = min.X;
                _y = min.Y;
                _z = min.Z;
                _slot = InvalidSlot;
                _cursorReady = false;
                _finished = false;
                Current = InvalidSlot;
            }

            public int Current { get; private set; }

            public bool MoveNext()
            {
                while (true)
                {
                    if (_slot != InvalidSlot)
                    {
                        Current = _slot;
                        _slot = _grid._next[_slot];
                        return true;
                    }

                    if (_finished)
                    {
                        return false;
                    }

                    if (!_cursorReady)
                    {
                        _cursorReady = true;
                    }
                    else if (_z < _max.Z)
                    {
                        _z++;
                    }
                    else if (_y < _max.Y)
                    {
                        _y++;
                        _z = _min.Z;
                    }
                    else if (_x < _max.X)
                    {
                        _x++;
                        _y = _min.Y;
                        _z = _min.Z;
                    }
                    else
                    {
                        _finished = true;
                        return false;
                    }

                    int bucket = _grid._buckets.Find(new CellCoordinate(_x, _y, _z));
                    _slot = bucket >= 0 ? _grid._buckets.HeadAt(bucket) : InvalidSlot;
                }
            }
        }
    }
}
