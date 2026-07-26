namespace CycloneGames.AIPerception.Networking
{
    /// <summary>
    /// Allocation-free operation counters for one stateless sync bridge. The bridge retains no
    /// snapshot entries, message history, queues, or payload buffers.
    /// </summary>
    public readonly struct AIPerceptionNetworkMemoryStats
    {
        public AIPerceptionNetworkMemoryStats(
            int profileMaximumSnapshotEntries,
            int profileMaximumSnapshotPayloadBytes,
            int maximumDetectionInputsPerWrite,
            long writeOperationCount,
            long suppliedDetectionCount,
            int peakSuppliedDetectionCount,
            long scannedDetectionCount,
            int peakScannedDetectionCount,
            long writtenEntryCount,
            long unresolvedCount,
            long invalidCount,
            long capacityLimitedCount,
            long duplicateCount,
            long snapshotOperationCount,
            long acceptedSnapshotCount,
            long rejectedSnapshotCount,
            int lastSnapshotEntryCount,
            int peakSnapshotEntryCount,
            int lastSnapshotPayloadBytes,
            int peakSnapshotPayloadBytes)
        {
            ProfileMaximumSnapshotEntries = profileMaximumSnapshotEntries;
            ProfileMaximumSnapshotPayloadBytes = profileMaximumSnapshotPayloadBytes;
            MaximumDetectionInputsPerWrite = maximumDetectionInputsPerWrite;
            WriteOperationCount = writeOperationCount;
            SuppliedDetectionCount = suppliedDetectionCount;
            PeakSuppliedDetectionCount = peakSuppliedDetectionCount;
            ScannedDetectionCount = scannedDetectionCount;
            PeakScannedDetectionCount = peakScannedDetectionCount;
            WrittenEntryCount = writtenEntryCount;
            UnresolvedCount = unresolvedCount;
            InvalidCount = invalidCount;
            CapacityLimitedCount = capacityLimitedCount;
            DuplicateCount = duplicateCount;
            SnapshotOperationCount = snapshotOperationCount;
            AcceptedSnapshotCount = acceptedSnapshotCount;
            RejectedSnapshotCount = rejectedSnapshotCount;
            LastSnapshotEntryCount = lastSnapshotEntryCount;
            PeakSnapshotEntryCount = peakSnapshotEntryCount;
            LastSnapshotPayloadBytes = lastSnapshotPayloadBytes;
            PeakSnapshotPayloadBytes = peakSnapshotPayloadBytes;
        }

        public int ProfileMaximumSnapshotEntries { get; }
        public int ProfileMaximumSnapshotPayloadBytes { get; }
        public int MaximumDetectionInputsPerWrite { get; }
        public long WriteOperationCount { get; }
        public long SuppliedDetectionCount { get; }
        public int PeakSuppliedDetectionCount { get; }
        public long ScannedDetectionCount { get; }
        public int PeakScannedDetectionCount { get; }
        public long WrittenEntryCount { get; }
        public long UnresolvedCount { get; }
        public long InvalidCount { get; }
        public long CapacityLimitedCount { get; }
        public long DuplicateCount { get; }
        public long DroppedEntryCount => SaturatingAdd(
            SaturatingAdd(UnresolvedCount, InvalidCount),
            SaturatingAdd(CapacityLimitedCount, DuplicateCount));
        public long SnapshotOperationCount { get; }
        public long AcceptedSnapshotCount { get; }
        public long RejectedSnapshotCount { get; }
        public int LastSnapshotEntryCount { get; }
        public int PeakSnapshotEntryCount { get; }
        public int LastSnapshotPayloadBytes { get; }
        public int PeakSnapshotPayloadBytes { get; }

        private static long SaturatingAdd(long left, long right)
        {
            return right > 0L && left > long.MaxValue - right ? long.MaxValue : left + right;
        }
    }
}
