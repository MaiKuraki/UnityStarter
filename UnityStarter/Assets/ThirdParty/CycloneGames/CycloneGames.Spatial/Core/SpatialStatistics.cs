namespace CycloneGames.Spatial.Core
{
    /// <summary>
    /// Zero-allocation snapshot of an index's counters.
    /// </summary>
    /// <remarks>
    /// Returned by value from <c>GetStatistics()</c>; cumulative counters are 64-bit. Allocates nothing.
    /// </remarks>
    public readonly struct SpatialStatistics
    {
        public SpatialStatistics(
            int elementCount,
            int occupiedCellCount,
            int slotCapacity,
            int bucketCapacity,
            int peakElementCount,
            int growthCount,
            int bucketRebuildCount,
            long totalInsertCount,
            long totalRemoveCount,
            long generationWrapCount)
        {
            ElementCount = elementCount;
            OccupiedCellCount = occupiedCellCount;
            SlotCapacity = slotCapacity;
            BucketCapacity = bucketCapacity;
            PeakElementCount = peakElementCount;
            GrowthCount = growthCount;
            BucketRebuildCount = bucketRebuildCount;
            TotalInsertCount = totalInsertCount;
            TotalRemoveCount = totalRemoveCount;
            GenerationWrapCount = generationWrapCount;
        }

        /// <summary>Elements currently stored.</summary>
        public int ElementCount { get; }

        /// <summary>Cells that currently hold at least one element.</summary>
        public int OccupiedCellCount { get; }

        /// <summary>Slots the index can hold without growing.</summary>
        public int SlotCapacity { get; }

        /// <summary>Buckets in the open-addressing table, including empty and tombstone buckets.</summary>
        public int BucketCapacity { get; }

        /// <summary>Highest <see cref="ElementCount"/> observed since construction.</summary>
        public int PeakElementCount { get; }

        /// <summary>Number of slot-array growth events. Stays flat once the capacity is sized correctly.</summary>
        public int GrowthCount { get; }

        /// <summary>
        /// Number of cell-bucket table rebuilds. Each rebuild rehashes every occupied bucket and is the hidden O(n)
        /// cost behind an insert that looks constant time, so watch this counter when diagnosing frame spikes.
        /// </summary>
        public int BucketRebuildCount { get; }

        /// <summary>Cumulative successful insertions.</summary>
        public long TotalInsertCount { get; }

        /// <summary>Cumulative successful removals.</summary>
        public long TotalRemoveCount { get; }

        /// <summary>
        /// Number of times a slot generation counter wrapped past <c>uint.MaxValue</c>.
        /// A non-zero value means a long-running host has recycled one slot more than 4 billion times.
        /// </summary>
        public long GenerationWrapCount { get; }
    }
}
