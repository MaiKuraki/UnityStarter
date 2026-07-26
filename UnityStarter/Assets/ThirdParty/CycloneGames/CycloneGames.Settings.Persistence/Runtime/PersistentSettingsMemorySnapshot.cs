namespace CycloneGames.Settings.Persistence
{
    /// <summary>Allocation-free operation and bounded-persistence attribution for one settings bridge.</summary>
    public readonly struct PersistentSettingsMemorySnapshot
    {
        public PersistentSettingsMemorySnapshot(
            bool isOperationActive,
            long stateRevision,
            bool persistenceOperationActive,
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
            StateRevision = stateRevision;
            PersistenceOperationActive = persistenceOperationActive;
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
        public long StateRevision { get; }
        public bool PersistenceOperationActive { get; }
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
