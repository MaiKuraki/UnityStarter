using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;

namespace CycloneGames.AIPerception.Runtime
{
    public sealed class SpatialGrid
    {
        private const int CELL_DIM_BITS = 20;
        private const int CELL_DIM_OFFSET = 1 << (CELL_DIM_BITS - 1); // 524288 — handles negative coords

        private readonly Dictionary<long, CellRange> _cellRanges = new Dictionary<long, CellRange>();
        private float _cellSize;

        private struct CellRange
        {
            public int StartIndex;
            public int Count;
        }

        public float CellSize => _cellSize;

        public SpatialGrid(float cellSize = 20f)
        {
            _cellSize = math.max(cellSize, 1f);
        }

        public void SetCellSize(float size)
        {
            _cellSize = math.max(size, 1f);
        }

        /// <summary>
        /// Sorts data by cell index in-place and builds cell boundary map.
        /// After rebuild, data is ordered by spatial locality — enabling zero-allocation contiguous-slice queries.
        /// </summary>
        public void Rebuild(PerceptibleData[] data, int count)
        {
            _cellRanges.Clear();

            if (count == 0) return;

            System.Array.Sort(data, 0, count, new CellKeyComparer(_cellSize));

            long currentKey = GetCellKey(data[0].Position, _cellSize);
            int cellStart = 0;

            for (int i = 1; i <= count; i++)
            {
                if (i == count || GetCellKey(data[i].Position, _cellSize) != currentKey)
                {
                    _cellRanges[currentKey] = new CellRange { StartIndex = cellStart, Count = i - cellStart };

                    if (i < count)
                    {
                        currentKey = GetCellKey(data[i].Position, _cellSize);
                        cellStart = i;
                    }
                }
            }
        }

        /// <summary>
        /// Creates a NativeArray from contiguous slices of spatially-ordered data.
        /// Zero intermediate allocations — no List, no index collection. CALLER MUST DISPOSE.
        /// </summary>
        public NativeArray<PerceptibleData> CreateFilteredCopy(
            PerceptibleData[] allData,
            int totalCount,
            float3 origin,
            float range,
            Allocator allocator = Allocator.TempJob)
        {
            if (_cellRanges.Count == 0 || totalCount == 0)
            {
                var full = new NativeArray<PerceptibleData>(math.max(totalCount, 1), allocator);
                for (int i = 0; i < totalCount; i++)
                    full[i] = allData[i];
                return full;
            }

            float invCellSize = 1f / _cellSize;
            int minCX = (int)math.floor((origin.x - range) * invCellSize);
            int maxCX = (int)math.floor((origin.x + range) * invCellSize);
            int minCY = (int)math.floor((origin.y - range) * invCellSize);
            int maxCY = (int)math.floor((origin.y + range) * invCellSize);
            int minCZ = (int)math.floor((origin.z - range) * invCellSize);
            int maxCZ = (int)math.floor((origin.z + range) * invCellSize);

            float rangeSq = range * range;
            int candidateCount = 0;

            for (int cx = minCX; cx <= maxCX; cx++)
            {
                for (int cy = minCY; cy <= maxCY; cy++)
                {
                    for (int cz = minCZ; cz <= maxCZ; cz++)
                    {
                        long key = PackCellKey(cx, cy, cz);
                        if (!_cellRanges.TryGetValue(key, out var cellRange)) continue;

                        for (int j = cellRange.StartIndex; j < cellRange.StartIndex + cellRange.Count; j++)
                        {
                            if (math.distancesq(allData[j].Position, origin) <= rangeSq)
                                candidateCount++;
                        }
                    }
                }
            }

            int resultCount = math.max(candidateCount, 1);
            var result = new NativeArray<PerceptibleData>(resultCount, allocator);
            int writeIdx = 0;

            for (int cx = minCX; cx <= maxCX; cx++)
            {
                for (int cy = minCY; cy <= maxCY; cy++)
                {
                    for (int cz = minCZ; cz <= maxCZ; cz++)
                    {
                        long key = PackCellKey(cx, cy, cz);
                        if (!_cellRanges.TryGetValue(key, out var cellRange)) continue;

                        for (int j = cellRange.StartIndex; j < cellRange.StartIndex + cellRange.Count; j++)
                        {
                            if (math.distancesq(allData[j].Position, origin) <= rangeSq)
                                result[writeIdx++] = allData[j];
                        }
                    }
                }
            }

            return result;
        }

        private static long PackCellKey(int cx, int cy, int cz)
        {
            ulong ux = (ulong)(cx + CELL_DIM_OFFSET) & ((1u << CELL_DIM_BITS) - 1);
            ulong uy = (ulong)(cy + CELL_DIM_OFFSET) & ((1u << CELL_DIM_BITS) - 1);
            ulong uz = (ulong)(cz + CELL_DIM_OFFSET) & ((1u << CELL_DIM_BITS) - 1);
            return (long)((ux << (CELL_DIM_BITS * 2)) | (uy << CELL_DIM_BITS) | uz);
        }

        private static long GetCellKey(float3 position, float cellSize)
        {
            float invCellSize = 1f / cellSize;
            int cx = (int)math.floor(position.x * invCellSize);
            int cy = (int)math.floor(position.y * invCellSize);
            int cz = (int)math.floor(position.z * invCellSize);
            return PackCellKey(cx, cy, cz);
        }

        public void Clear()
        {
            _cellRanges.Clear();
        }

        private sealed class CellKeyComparer : IComparer<PerceptibleData>
        {
            private readonly float _cellSize;

            public CellKeyComparer(float cellSize)
            {
                _cellSize = cellSize;
            }

            public int Compare(PerceptibleData x, PerceptibleData y)
            {
                long keyX = GetCellKey(x.Position);
                long keyY = GetCellKey(y.Position);
                return keyX.CompareTo(keyY);
            }

            private long GetCellKey(float3 position)
            {
                return SpatialGrid.GetCellKey(position, _cellSize);
            }
        }
    }
}
