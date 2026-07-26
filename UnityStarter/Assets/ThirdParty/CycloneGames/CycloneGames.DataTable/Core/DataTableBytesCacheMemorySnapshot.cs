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
            DataTableLoadLimits limits,
            long releasedPayloadCount,
            long releasedBytes)
        {
            PayloadCount = payloadCount;
            TotalBytes = totalBytes;
            IsSealed = isSealed;
            IsClosed = isClosed;
            Limits = limits;
            ReleasedPayloadCount = releasedPayloadCount;
            ReleasedBytes = releasedBytes;
        }

        public int PayloadCount { get; }

        public long TotalBytes { get; }

        public bool IsSealed { get; }

        public bool IsClosed { get; }

        public DataTableLoadLimits Limits { get; }

        public long ReleasedPayloadCount { get; }

        public long ReleasedBytes { get; }
    }

    /// <summary>Result of one bounded release pass over a closed payload cache.</summary>
    public readonly struct DataTableBytesCacheReleaseResult
    {
        internal DataTableBytesCacheReleaseResult(
            int workConsumed,
            long releasedBytes,
            int remainingPayloadCount,
            long remainingBytes)
        {
            WorkConsumed = workConsumed;
            ReleasedBytes = releasedBytes;
            RemainingPayloadCount = remainingPayloadCount;
            RemainingBytes = remainingBytes;
        }

        public int WorkConsumed { get; }

        public long ReleasedBytes { get; }

        public int RemainingPayloadCount { get; }

        public long RemainingBytes { get; }

        public bool HasMorePayloads => RemainingPayloadCount > 0;
    }
}
