namespace CycloneGames.DataTable
{
    /// <summary>Allocation-free snapshot of one explicitly owned payload cache.</summary>
    public readonly struct DataTableBytesCacheMemorySnapshot
    {
        internal DataTableBytesCacheMemorySnapshot(
            int payloadCount,
            long totalBytes,
            bool isSealed,
            bool isClosed,
            bool isReleaseComplete,
            DataTableLoadLimits limits,
            long releasedPayloadCount,
            long releasedBytes,
            long clearedBytes)
        {
            PayloadCount = payloadCount;
            TotalBytes = totalBytes;
            IsSealed = isSealed;
            IsClosed = isClosed;
            IsReleaseComplete = isReleaseComplete;
            Limits = limits;
            ReleasedPayloadCount = releasedPayloadCount;
            ReleasedBytes = releasedBytes;
            ClearedBytes = clearedBytes;
        }

        public int PayloadCount { get; }

        public long TotalBytes { get; }

        public bool IsSealed { get; }

        public bool IsClosed { get; }

        public bool IsReleaseComplete { get; }

        public DataTableLoadLimits Limits { get; }

        public long ReleasedPayloadCount { get; }

        public long ReleasedBytes { get; }

        public long ClearedBytes { get; }
    }
}
