using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;

namespace CycloneGames.AIPerception.Runtime
{
    /// <summary>
    /// Main-thread spatial broadphase over the immutable registry snapshot. Queries write indices
    /// into caller-owned persistent storage and fall back to a linear scan for very large bounds.
    /// </summary>
    /// <remarks>
    /// Cell coordinates are packed into a 63-bit key once per element, and the rebuild sorts an
    /// index permutation rather than the payload. Both choices exist to keep a dirty rebuild inside
    /// one frame: the comparer never re-derives coordinates, and it never copies a 48-byte element.
    /// </remarks>
    public sealed class SpatialGrid
    {
        private const long MaximumCellVisitsPerQuery = 65536L;

        /// <summary>Bits per axis in the packed cell key. Three axes occupy 63 bits.</summary>
        private const int AxisBits = 21;

        /// <summary>Half of the addressable cell span; cells outside the span collapse to the edge.</summary>
        private const int AxisLimit = 1 << (AxisBits - 1);

        private const int AxisMask = (1 << AxisBits) - 1;

        /// <summary>
        /// Orders element indices instead of the 48-byte payload. Sorting indices keeps the
        /// comparison cost at two 8-byte key reads plus two 4-byte tie-break reads; sorting the
        /// payload directly copies 48 bytes per comparison and dominates a large rebuild.
        /// </summary>
        private struct CellRange
        {
            public int StartIndex;
            public int Count;
        }

        private sealed class IndexComparer : IComparer<int>
        {
            public long[] CellKeys;
            public int[] TieBreakers;
            public PerceptibleData[] Data;

            public int Compare(int left, int right)
            {
                long leftKey = CellKeys[left];
                long rightKey = CellKeys[right];
                if (leftKey != rightKey)
                {
                    return leftKey < rightKey ? -1 : 1;
                }

                int leftTie = TieBreakers[left];
                int rightTie = TieBreakers[right];
                if (leftTie != rightTie)
                {
                    return leftTie < rightTie ? -1 : 1;
                }

                // Same slot twice is invalid input; fall back to the full ordering so the result
                // stays deterministic instead of silently following input order.
                PerceptibleData leftData = Data[left];
                PerceptibleData rightData = Data[right];
                int registry = leftData.RegistryId.CompareTo(rightData.RegistryId);
                if (registry != 0)
                {
                    return registry;
                }

                return leftData.Generation.CompareTo(rightData.Generation);
            }
        }

        private readonly Dictionary<long, CellRange> _cellRanges = new Dictionary<long, CellRange>(256);
        private readonly IndexComparer _indexComparer = new IndexComparer();
        private long[] _keys = Array.Empty<long>();
        private long[] _sortedKeys = Array.Empty<long>();
        private int[] _order = Array.Empty<int>();
        private int[] _tieBreakers = Array.Empty<int>();
        private PerceptibleData[] _scratch = Array.Empty<PerceptibleData>();
        private float _cellSize;

        public float CellSize => _cellSize;
        internal int CellCount => _cellRanges.Count;

        public SpatialGrid(float cellSize = 20f)
        {
            SetCellSize(cellSize);
        }

        public void SetCellSize(float size)
        {
            if (!math.isfinite(size) || size <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(size), "Spatial-grid cell size must be finite and positive.");
            }

            _cellSize = size;
        }

        /// <summary>
        /// Sorts the active snapshot by cell and stable handle, then records contiguous cell ranges.
        /// </summary>
        public void Rebuild(PerceptibleData[] data, int count)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (count < 0 || count > data.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            _cellRanges.Clear();
            if (count == 0)
            {
                return;
            }

            EnsureCapacity(count);
            for (int i = 0; i < count; i++)
            {
                _keys[i] = GetCellKey(data[i].Position, _cellSize);
                _tieBreakers[i] = data[i].Id;
                _order[i] = i;
            }

            _indexComparer.Data = data;
            Array.Sort(_order, 0, count, _indexComparer);

            for (int i = 0; i < count; i++)
            {
                int source = _order[i];
                _sortedKeys[i] = _keys[source];
                _scratch[i] = data[source];
            }

            Array.Copy(_scratch, 0, data, 0, count);

            long current = _sortedKeys[0];
            int cellStart = 0;
            for (int i = 1; i <= count; i++)
            {
                if (i != count && _sortedKeys[i] == current)
                {
                    continue;
                }

                _cellRanges.Add(current, new CellRange { StartIndex = cellStart, Count = i - cellStart });
                if (i < count)
                {
                    current = _sortedKeys[i];
                    cellStart = i;
                }
            }
        }

        /// <summary>
        /// Writes exact center-distance candidates to <paramref name="results"/>. Returns false and
        /// clears the list when the configured hard capacity would be exceeded.
        /// </summary>
        public bool CollectIndices(
            PerceptibleData[] allData,
            int totalCount,
            float3 origin,
            float range,
            ref NativeList<int> results,
            int maximumResults)
        {
            if (allData == null)
            {
                throw new ArgumentNullException(nameof(allData));
            }

            if (totalCount < 0 || totalCount > allData.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(totalCount));
            }

            if (!results.IsCreated)
            {
                throw new ArgumentException("Candidate storage must be created by the sensor owner.", nameof(results));
            }

            results.Clear();
            if (totalCount == 0)
            {
                return true;
            }

            if (!math.all(math.isfinite(origin)) || !math.isfinite(range) || range < 0f || maximumResults <= 0)
            {
                return false;
            }

            float3 minimumPosition = origin - range;
            float3 maximumPosition = origin + range;
            int minimumX = GetCellAxis(minimumPosition.x, _cellSize);
            int minimumY = GetCellAxis(minimumPosition.y, _cellSize);
            int minimumZ = GetCellAxis(minimumPosition.z, _cellSize);
            int maximumX = GetCellAxis(maximumPosition.x, _cellSize);
            int maximumY = GetCellAxis(maximumPosition.y, _cellSize);
            int maximumZ = GetCellAxis(maximumPosition.z, _cellSize);

            long cellCount = SaturatingProduct(
                InclusiveLength(minimumX, maximumX),
                InclusiveLength(minimumY, maximumY),
                InclusiveLength(minimumZ, maximumZ));
            float rangeSquared = range * range;
            bool useDoubleDistance = !math.isfinite(rangeSquared);
            double rangeSquaredDouble = (double)range * range;

            if (_cellRanges.Count == 0 || cellCount > MaximumCellVisitsPerQuery)
            {
                return CollectLinear(
                    allData,
                    totalCount,
                    origin,
                    rangeSquared,
                    rangeSquaredDouble,
                    useDoubleDistance,
                    ref results,
                    maximumResults);
            }

            int startX = ClampAxis(minimumX);
            int startY = ClampAxis(minimumY);
            int startZ = ClampAxis(minimumZ);
            int endX = ClampAxis(maximumX);
            int endY = ClampAxis(maximumY);
            int endZ = ClampAxis(maximumZ);

            for (int x = startX; ; x++)
            {
                for (int y = startY; ; y++)
                {
                    for (int z = startZ; ; z++)
                    {
                        long key = PackCell(x, y, z);
                        if (_cellRanges.TryGetValue(key, out CellRange cellRange))
                        {
                            int end = cellRange.StartIndex + cellRange.Count;
                            for (int i = cellRange.StartIndex; i < end; i++)
                            {
                                if (IsWithinRange(
                                        allData[i].Position,
                                        origin,
                                        rangeSquared,
                                        rangeSquaredDouble,
                                        useDoubleDistance))
                                {
                                    if (results.Length >= maximumResults)
                                    {
                                        results.Clear();
                                        return false;
                                    }

                                    results.Add(i);
                                }
                            }
                        }

                        if (z == endZ)
                        {
                            break;
                        }
                    }

                    if (y == endY)
                    {
                        break;
                    }
                }

                if (x == endX)
                {
                    break;
                }
            }

            return true;
        }

        public void Clear()
        {
            _cellRanges.Clear();
        }

        private void EnsureCapacity(int count)
        {
            if (_order.Length >= count)
            {
                return;
            }

            int capacity = math.max(64, _order.Length);
            while (capacity < count)
            {
                capacity *= 2;
            }

            Array.Resize(ref _order, capacity);
            Array.Resize(ref _keys, capacity);
            Array.Resize(ref _sortedKeys, capacity);
            Array.Resize(ref _tieBreakers, capacity);
            Array.Resize(ref _scratch, capacity);
            _indexComparer.CellKeys = _keys;
            _indexComparer.TieBreakers = _tieBreakers;
        }

        private static bool CollectLinear(
            PerceptibleData[] data,
            int count,
            float3 origin,
            float rangeSquared,
            double rangeSquaredDouble,
            bool useDoubleDistance,
            ref NativeList<int> results,
            int maximumResults)
        {
            for (int i = 0; i < count; i++)
            {
                if (!IsWithinRange(
                        data[i].Position,
                        origin,
                        rangeSquared,
                        rangeSquaredDouble,
                        useDoubleDistance))
                {
                    continue;
                }

                if (results.Length >= maximumResults)
                {
                    results.Clear();
                    return false;
                }

                results.Add(i);
            }

            return true;
        }

        private static bool IsWithinRange(
            float3 position,
            float3 origin,
            float rangeSquared,
            double rangeSquaredDouble,
            bool useDoubleDistance)
        {
            if (!math.all(math.isfinite(position)))
            {
                return false;
            }

            if (!useDoubleDistance)
            {
                float distanceSquared = math.distancesq(position, origin);
                return math.isfinite(distanceSquared) && distanceSquared <= rangeSquared;
            }

            double x = (double)position.x - origin.x;
            double y = (double)position.y - origin.y;
            double z = (double)position.z - origin.z;
            return (x * x) + (y * y) + (z * z) <= rangeSquaredDouble;
        }

        private static long GetCellKey(float3 position, float cellSize)
        {
            return PackCell(
                GetCellAxis(position.x, cellSize),
                GetCellAxis(position.y, cellSize),
                GetCellAxis(position.z, cellSize));
        }

        private static int GetCellAxis(float value, float cellSize)
        {
            if (!math.isfinite(value))
            {
                return value > 0f ? int.MaxValue : int.MinValue;
            }

            double normalized = (double)value * (1d / cellSize);
            if (normalized <= int.MinValue)
            {
                return int.MinValue;
            }

            if (normalized >= int.MaxValue)
            {
                return int.MaxValue;
            }

            return (int)Math.Floor(normalized);
        }

        private static int ClampAxis(int value)
        {
            if (value < -AxisLimit)
            {
                return -AxisLimit;
            }

            return value > AxisLimit - 1 ? AxisLimit - 1 : value;
        }

        private static long PackCell(int x, int y, int z)
        {
            long packedX = (long)(ClampAxis(x) + AxisLimit) & AxisMask;
            long packedY = (long)(ClampAxis(y) + AxisLimit) & AxisMask;
            long packedZ = (long)(ClampAxis(z) + AxisLimit) & AxisMask;
            return (packedX << (AxisBits * 2)) | (packedY << AxisBits) | packedZ;
        }

        private static long InclusiveLength(int minimum, int maximum)
        {
            return (long)maximum - minimum + 1L;
        }

        private static long SaturatingProduct(long x, long y, long z)
        {
            if (x <= 0L || y <= 0L || z <= 0L || x > MaximumCellVisitsPerQuery)
            {
                return long.MaxValue;
            }

            long xy = x * y;
            if (xy > MaximumCellVisitsPerQuery || z > MaximumCellVisitsPerQuery / xy)
            {
                return long.MaxValue;
            }

            return xy * z;
        }
    }
}
