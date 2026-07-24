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
    public sealed class SpatialGrid
    {
        private const long MaximumCellVisitsPerQuery = 65536L;

        private readonly struct CellCoordinate : IEquatable<CellCoordinate>
        {
            public CellCoordinate(int x, int y, int z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            public int X { get; }
            public int Y { get; }
            public int Z { get; }

            public bool Equals(CellCoordinate other) => X == other.X && Y == other.Y && Z == other.Z;
            public override bool Equals(object obj) => obj is CellCoordinate other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(X, Y, Z);
        }

        private struct CellRange
        {
            public int StartIndex;
            public int Count;
        }

        private sealed class CellComparer : IComparer<PerceptibleData>
        {
            public float CellSize { get; set; }

            public int Compare(PerceptibleData left, PerceptibleData right)
            {
                CellCoordinate leftCell = GetCell(left.Position, CellSize);
                CellCoordinate rightCell = GetCell(right.Position, CellSize);
                int x = leftCell.X.CompareTo(rightCell.X);
                if (x != 0)
                {
                    return x;
                }

                int y = leftCell.Y.CompareTo(rightCell.Y);
                if (y != 0)
                {
                    return y;
                }

                int z = leftCell.Z.CompareTo(rightCell.Z);
                if (z != 0)
                {
                    return z;
                }

                int registry = left.RegistryId.CompareTo(right.RegistryId);
                if (registry != 0)
                {
                    return registry;
                }

                int id = left.Id.CompareTo(right.Id);
                return id != 0 ? id : left.Generation.CompareTo(right.Generation);
            }
        }

        private readonly Dictionary<CellCoordinate, CellRange> _cellRanges =
            new Dictionary<CellCoordinate, CellRange>(256);
        private readonly CellComparer _comparer = new CellComparer();
        private float _cellSize;

        public float CellSize => _cellSize;

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
            _comparer.CellSize = _cellSize;
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

            Array.Sort(data, 0, count, _comparer);

            CellCoordinate current = GetCell(data[0].Position, _cellSize);
            int cellStart = 0;
            for (int i = 1; i <= count; i++)
            {
                if (i != count)
                {
                    CellCoordinate candidate = GetCell(data[i].Position, _cellSize);
                    if (candidate.Equals(current))
                    {
                        continue;
                    }
                }

                _cellRanges.Add(current, new CellRange { StartIndex = cellStart, Count = i - cellStart });
                if (i < count)
                {
                    current = GetCell(data[i].Position, _cellSize);
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

            CellCoordinate minimum = GetCell(origin - range, _cellSize);
            CellCoordinate maximum = GetCell(origin + range, _cellSize);
            long cellCount = SaturatingProduct(
                InclusiveLength(minimum.X, maximum.X),
                InclusiveLength(minimum.Y, maximum.Y),
                InclusiveLength(minimum.Z, maximum.Z));
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

            for (int x = minimum.X; ; x++)
            {
                for (int y = minimum.Y; ; y++)
                {
                    for (int z = minimum.Z; ; z++)
                    {
                        if (_cellRanges.TryGetValue(new CellCoordinate(x, y, z), out CellRange cellRange))
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

                        if (z == maximum.Z)
                        {
                            break;
                        }
                    }

                    if (y == maximum.Y)
                    {
                        break;
                    }
                }

                if (x == maximum.X)
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

        private static CellCoordinate GetCell(float3 position, float cellSize)
        {
            double inverseCellSize = 1d / cellSize;
            return new CellCoordinate(
                FloorToInt((double)position.x * inverseCellSize),
                FloorToInt((double)position.y * inverseCellSize),
                FloorToInt((double)position.z * inverseCellSize));
        }

        private static int FloorToInt(double value)
        {
            if (value <= int.MinValue)
            {
                return int.MinValue;
            }

            if (value >= int.MaxValue)
            {
                return int.MaxValue;
            }

            return (int)Math.Floor(value);
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
