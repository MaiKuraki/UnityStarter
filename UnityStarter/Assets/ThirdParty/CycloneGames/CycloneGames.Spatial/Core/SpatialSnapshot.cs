using System;
using CycloneGames.Spatial.Core.Internal;

namespace CycloneGames.Spatial.Core
{
    /// <summary>
    /// Immutable spatial index built in bulk and queried many times.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Storage is a sorted array of packed cell keys plus a permutation into the element arrays. A query binary
    /// searches a key, then walks the contiguous run; there is no bucket table and no chain.
    /// </para>
    /// <para>Build is O(n log n) and allocates; queries do not. Rebuild on a load or tick boundary, not mid-frame.</para>
    /// <para>Single writer: <see cref="Build"/> and every query must run on the same thread.</para>
    /// </remarks>
    /// <typeparam name="TItem">Payload returned by queries.</typeparam>
    public sealed class SpatialSnapshot<TItem> : ISpatialReadOnlyIndex<TItem>, IDisposable
    {
        /// <summary>Bits reserved per axis in the packed cell key. Three axes fit in 63 bits.</summary>
        private const int AxisBits = 21;

        /// <summary>Half of the addressable cell span; cells outside <c>[-AxisLimit, AxisLimit)</c> are rejected.</summary>
        private const int AxisLimit = 1 << (AxisBits - 1);

        private const float DistanceTolerance = 1e-5f;
        private const float BoxTolerance = 1e-4f;

        private readonly GridSettings _settings;

        private TItem[] _items;
        private float[] _positions;
        private uint[] _layers;
        private long[] _cellKeys;
        private int[] _order;

        private int _count;
        private int _capacity;
        private long _buildCount;
        private bool _disposed;

        /// <summary>
        /// Creates an empty snapshot.
        /// </summary>
        /// <param name="settings">Cell size and origin. Fixed for the lifetime of the instance; rebuild to change it.</param>
        /// <param name="capacity">Entries to reserve. <see cref="Build"/> grows the arrays when it needs to.</param>
        public SpatialSnapshot(GridSettings settings, int capacity = 64)
        {
            if (capacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity cannot be negative.");
            }

            _settings = settings;
            _capacity = capacity < 16 ? 16 : capacity;
            _items = new TItem[_capacity];
            _positions = new float[_capacity * 3];
            _layers = new uint[_capacity];
            _cellKeys = new long[_capacity];
            _order = new int[_capacity];
        }

        /// <summary>Cell size and origin in use.</summary>
        public GridSettings Settings
        {
            get { return _settings; }
        }

        /// <summary>Entries currently in the snapshot.</summary>
        public int Count
        {
            get { return _count; }
        }

        /// <summary>Entries that fit without growing.</summary>
        public int Capacity
        {
            get { return _capacity; }
        }

        /// <summary>Number of <see cref="Build"/> calls that produced a non-empty snapshot.</summary>
        public long BuildCount
        {
            get { return _buildCount; }
        }

        /// <summary>True after <see cref="Dispose"/>.</summary>
        public bool IsDisposed
        {
            get { return _disposed; }
        }

        /// <summary>
        /// Addressable cell span on one axis, in cells. Centred on the grid origin.
        /// </summary>
        /// <remarks>
        /// The packed key reserves 21 bits per axis, so a snapshot addresses <c>2 * AxisLimitCells</c> cells per axis
        /// (about 2097 km at a one-metre cell size).
        /// </remarks>
        public static int AxisLimitCells
        {
            get { return AxisLimit; }
        }

        /// <summary>
        /// Replaces the contents with the supplied entries.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Complexity: O(n log n). Allocates only when <paramref name="entries"/> exceeds the current capacity; a
        /// same-size rebuild reuses the arrays.
        /// </para>
        /// <para>An empty span produces an empty snapshot. A failed build leaves the previous contents intact.</para>
        /// </remarks>
        /// <exception cref="ObjectDisposedException">The snapshot has been disposed.</exception>
        /// <exception cref="ArgumentException">An entry has a non-finite position.</exception>
        /// <exception cref="ArgumentOutOfRangeException">
        /// An entry maps to a cell outside the addressable span. Increase the cell size or rebase the origin.
        /// </exception>
        public void Build(ReadOnlySpan<SpatialEntry<TItem>> entries)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(SpatialSnapshot<TItem>));
            }

            int count = entries.Length;

            // Validate first: a partially applied build would leave _cellKeys unsorted while _count still describes the
            // previous contents, and every later query would binary search it.
            for (int i = 0; i < count; i++)
            {
                SpatialEntry<TItem> entry = entries[i];
                if (!IsFinitePosition(entry.X, entry.Y, entry.Z))
                {
                    throw new ArgumentException("Entry " + i + " has a non-finite position.", nameof(entries));
                }

                CellCoordinate cell = _settings.WorldToCell(entry.X, entry.Y, entry.Z);
                if (!TryPackCell(cell.X, cell.Y, cell.Z, out _))
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(entries),
                        "Entry " + i + " maps to cell " + cell + ", outside the snapshot addressable span of " +
                        (-AxisLimit) + " to " + (AxisLimit - 1) + " per axis. Increase the cell size or rebase the origin.");
                }
            }

            EnsureCapacity(count);

            for (int i = 0; i < count; i++)
            {
                SpatialEntry<TItem> entry = entries[i];
                CellCoordinate cell = _settings.WorldToCell(entry.X, entry.Y, entry.Z);
                TryPackCell(cell.X, cell.Y, cell.Z, out long key);

                _items[i] = entry.Item;
                _layers[i] = entry.LayerMask == 0u ? SpatialLayers.Default : entry.LayerMask;
                int offset = i * 3;
                _positions[offset] = entry.X;
                _positions[offset + 1] = entry.Y;
                _positions[offset + 2] = entry.Z;
                _cellKeys[i] = key;
                _order[i] = i;
            }

            _count = count;
            if (count > 1)
            {
                Array.Sort(_cellKeys, _order, 0, count);
            }

            if (count > 0)
            {
                _buildCount++;
            }
        }

        /// <summary>Empties the snapshot while keeping the arrays.</summary>
        public void Clear()
        {
            if (_disposed)
            {
                return;
            }

            _count = 0;
        }

        /// <inheritdoc/>
        public int QuerySphere(
            float centerX,
            float centerY,
            float centerZ,
            float radius,
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
            var cursor = new CellKeyCursor(minCell, maxCell);
            while (cursor.MoveNext())
            {
                int start;
                int end;
                if (!TryFindRange(cursor.Current, out start, out end))
                {
                    continue;
                }

                for (int i = start; i < end; i++)
                {
                    int index = _order[i];
                    if ((_layers[index] & layerMask) == 0u)
                    {
                        continue;
                    }

                    int offset = index * 3;
                    float dx = _positions[offset] - centerX;
                    float dy = _positions[offset + 1] - centerY;
                    float dz = _positions[offset + 2] - centerZ;
                    if ((dx * dx) + (dy * dy) + (dz * dz) > radiusSquared)
                    {
                        continue;
                    }

                    results[written] = _items[index];
                    written++;
                    if (written >= limit)
                    {
                        return written;
                    }
                }
            }

            return written;
        }

        /// <inheritdoc/>
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
            var cursor = new CellKeyCursor(minCell, maxCell);
            while (cursor.MoveNext())
            {
                int start;
                int end;
                if (!TryFindRange(cursor.Current, out start, out end))
                {
                    continue;
                }

                for (int i = start; i < end; i++)
                {
                    int index = _order[i];
                    if ((_layers[index] & layerMask) == 0u || !IsInsideBox(index, minX, minY, minZ, maxX, maxY, maxZ))
                    {
                        continue;
                    }

                    results[written] = _items[index];
                    written++;
                    if (written >= limit)
                    {
                        return written;
                    }
                }
            }

            return written;
        }

        /// <inheritdoc/>
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
                return QueryNearestLinear(centerX, centerY, centerZ, radiusSquared, layerMask, limit, results, squaredDistances);
            }

            int count = 0;
            var cursor = new CellKeyCursor(minCell, maxCell);
            while (cursor.MoveNext())
            {
                int start;
                int end;
                if (!TryFindRange(cursor.Current, out start, out end))
                {
                    continue;
                }

                for (int i = start; i < end; i++)
                {
                    int index = _order[i];
                    if ((_layers[index] & layerMask) == 0u)
                    {
                        continue;
                    }

                    int offset = index * 3;
                    float dx = _positions[offset] - centerX;
                    float dy = _positions[offset + 1] - centerY;
                    float dz = _positions[offset + 2] - centerZ;
                    float distanceSquared = (dx * dx) + (dy * dy) + (dz * dz);
                    if (distanceSquared > radiusSquared)
                    {
                        continue;
                    }

                    count = NearestBuffer.Insert(count, limit, distanceSquared, _items[index], results, squaredDistances);
                }
            }

            return count;
        }

        /// <summary>Releases every array reference so the snapshot can be collected.</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _items = null;
            _positions = null;
            _layers = null;
            _cellKeys = null;
            _order = null;
            _count = 0;
            _capacity = 0;
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
            for (int index = 0; index < _count; index++)
            {
                if ((_layers[index] & layerMask) == 0u)
                {
                    continue;
                }

                int offset = index * 3;
                float dx = _positions[offset] - centerX;
                float dy = _positions[offset + 1] - centerY;
                float dz = _positions[offset + 2] - centerZ;
                if ((dx * dx) + (dy * dy) + (dz * dz) > radiusSquared)
                {
                    continue;
                }

                results[written] = _items[index];
                written++;
                if (written >= limit)
                {
                    return written;
                }
            }

            return written;
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
            for (int index = 0; index < _count; index++)
            {
                if ((_layers[index] & layerMask) == 0u || !IsInsideBox(index, minX, minY, minZ, maxX, maxY, maxZ))
                {
                    continue;
                }

                results[written] = _items[index];
                written++;
                if (written >= limit)
                {
                    return written;
                }
            }

            return written;
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
            for (int index = 0; index < _count; index++)
            {
                if ((_layers[index] & layerMask) == 0u)
                {
                    continue;
                }

                int offset = index * 3;
                float dx = _positions[offset] - centerX;
                float dy = _positions[offset + 1] - centerY;
                float dz = _positions[offset + 2] - centerZ;
                float distanceSquared = (dx * dx) + (dy * dy) + (dz * dz);
                if (distanceSquared > radiusSquared)
                {
                    continue;
                }

                count = NearestBuffer.Insert(count, limit, distanceSquared, _items[index], results, squaredDistances);
            }

            return count;
        }

        /// <summary>
        /// Finds the contiguous run of entries whose packed cell key equals <paramref name="key"/>.
        /// </summary>
        /// <remarks>Complexity: O(log n) for the lower bound plus O(run length) to find the end.</remarks>
        private bool TryFindRange(long key, out int start, out int end)
        {
            int low = 0;
            int high = _count;
            while (low < high)
            {
                int middle = low + ((high - low) >> 1);
                if (_cellKeys[middle] < key)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            if (low >= _count || _cellKeys[low] != key)
            {
                start = 0;
                end = 0;
                return false;
            }

            int upper = low + 1;
            while (upper < _count && _cellKeys[upper] == key)
            {
                upper++;
            }

            start = low;
            end = upper;
            return true;
        }

        private bool IsInsideBox(int index, float minX, float minY, float minZ, float maxX, float maxY, float maxZ)
        {
            int offset = index * 3;
            float x = _positions[offset];
            float y = _positions[offset + 1];
            float z = _positions[offset + 2];
            return x >= minX - BoxTolerance && x <= maxX + BoxTolerance &&
                   y >= minY - BoxTolerance && y <= maxY + BoxTolerance &&
                   z >= minZ - BoxTolerance && z <= maxZ + BoxTolerance;
        }

        private void EnsureCapacity(int count)
        {
            if (count <= _capacity)
            {
                return;
            }

            int target = _capacity;
            while (target < count)
            {
                target *= 2;
            }

            Array.Resize(ref _items, target);
            Array.Resize(ref _layers, target);
            Array.Resize(ref _cellKeys, target);
            Array.Resize(ref _order, target);

            var positions = new float[target * 3];
            Array.Copy(_positions, positions, _capacity * 3);
            _positions = positions;

            _capacity = target;
        }

        /// <summary>
        /// Packs a cell into a monotonically ordered 63-bit key.
        /// </summary>
        /// <remarks>
        /// Each axis is biased into the unsigned 21-bit range, so numeric key order equals cell order (X, then Y, then
        /// Z). That allows a plain <c>Array.Sort</c> with no comparison callback.
        /// </remarks>
        private static bool TryPackCell(int x, int y, int z, out long key)
        {
            if (x <= -AxisLimit || x >= AxisLimit ||
                y <= -AxisLimit || y >= AxisLimit ||
                z <= -AxisLimit || z >= AxisLimit)
            {
                key = 0L;
                return false;
            }

            long packedX = x + (long)AxisLimit;
            long packedY = y + (long)AxisLimit;
            long packedZ = z + (long)AxisLimit;
            key = (packedX << (AxisBits * 2)) | (packedY << AxisBits) | packedZ;
            return true;
        }

        private static bool IsFinitePosition(float x, float y, float z)
        {
            return GridSettings.IsFinite(x) && GridSettings.IsFinite(y) && GridSettings.IsFinite(z);
        }

        /// <summary>
        /// Walks every cell of an inclusive range and yields its packed key.
        /// </summary>
        /// <remarks>
        /// Cells outside the addressable span are skipped rather than clamped: nothing can be stored there, so there is
        /// nothing to find.
        /// </remarks>
        private struct CellKeyCursor
        {
            private readonly CellCoordinate _min;
            private readonly CellCoordinate _max;

            private int _x;
            private int _y;
            private int _z;
            private bool _started;
            private bool _finished;

            public CellKeyCursor(CellCoordinate min, CellCoordinate max)
            {
                _min = min;
                _max = max;
                _x = min.X;
                _y = min.Y;
                _z = min.Z;
                _started = false;
                _finished = false;
                Current = 0L;
            }

            /// <summary>Packed key of the current cell.</summary>
            public long Current { get; private set; }

            public bool MoveNext()
            {
                if (_finished)
                {
                    return false;
                }

                if (!_started)
                {
                    _started = true;
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

                if (!TryPackCell(_x, _y, _z, out long key))
                {
                    // Out of span: no entry can live there, but the cursor must still advance next time.
                    Current = 0L;
                    return MoveNextSkippingUnaddressable();
                }

                Current = key;
                return true;
            }

            private bool MoveNextSkippingUnaddressable()
            {
                // Keep advancing until an addressable cell is found or the range ends. Ranges are budget limited, so
                // the recursion depth here is bounded by the range length and never grows the stack per element.
                while (!_finished)
                {
                    if (_z < _max.Z)
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

                    if (TryPackCell(_x, _y, _z, out long key))
                    {
                        Current = key;
                        return true;
                    }
                }

                return false;
            }
        }
    }
}
