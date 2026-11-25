namespace CycloneGames.Persistence
{
    public readonly struct PersistenceStoreMemorySnapshot
    {
        public PersistenceStoreMemorySnapshot(
            bool isOperationActive,
            int maximumPayloadBytes,
            int maximumRecordBytes,
            long startedLoadCount,
            long startedSaveCount,
            long startedDeleteCount,
            long concurrentOperationRejectionCount,
            long lastRecordBytes,
            long peakRecordBytes)
        {
            IsOperationActive = isOperationActive;
            MaximumPayloadBytes = maximumPayloadBytes;
            MaximumRecordBytes = maximumRecordBytes;
            StartedLoadCount = startedLoadCount;
            StartedSaveCount = startedSaveCount;
            StartedDeleteCount = startedDeleteCount;
            ConcurrentOperationRejectionCount = concurrentOperationRejectionCount;
            LastRecordBytes = lastRecordBytes;
            PeakRecordBytes = peakRecordBytes;
        }

        public bool IsOperationActive { get; }

        public int MaximumPayloadBytes { get; }

        public int MaximumRecordBytes { get; }

        public long StartedLoadCount { get; }

        public long StartedSaveCount { get; }

        public long StartedDeleteCount { get; }

        public long ConcurrentOperationRejectionCount { get; }

        public long LastRecordBytes { get; }

        public long PeakRecordBytes { get; }
    }
}
