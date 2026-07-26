namespace CycloneGames.Choreography.AssetManagement
{
    /// <summary>Bridge-only ownership counters. Asset payload bytes remain owned by AssetManagement.</summary>
    public readonly struct ChoreographyAssetManagementMemoryStats
    {
        public ChoreographyAssetManagementMemoryStats(
            int retainedRequestCount,
            int maximumRetainedRequestCount,
            int activeLeaseCount,
            int pendingRequestCount,
            int peakRetainedRequestCount,
            int peakActiveLeaseCount,
            int peakPendingRequestCount,
            long loadRequestCount,
            long backendRequestCount,
            long reusedLeaseCount,
            long failedRequestCount,
            long rejectedRequestCount,
            long releasedRequestCount)
        {
            RetainedRequestCount = retainedRequestCount;
            MaximumRetainedRequestCount = maximumRetainedRequestCount;
            ActiveLeaseCount = activeLeaseCount;
            PendingRequestCount = pendingRequestCount;
            PeakRetainedRequestCount = peakRetainedRequestCount;
            PeakActiveLeaseCount = peakActiveLeaseCount;
            PeakPendingRequestCount = peakPendingRequestCount;
            LoadRequestCount = loadRequestCount;
            BackendRequestCount = backendRequestCount;
            ReusedLeaseCount = reusedLeaseCount;
            FailedRequestCount = failedRequestCount;
            RejectedRequestCount = rejectedRequestCount;
            ReleasedRequestCount = releasedRequestCount;
        }

        public int RetainedRequestCount { get; }
        public int MaximumRetainedRequestCount { get; }
        public int ActiveLeaseCount { get; }
        public int PendingRequestCount { get; }
        public int PeakRetainedRequestCount { get; }
        public int PeakActiveLeaseCount { get; }
        public int PeakPendingRequestCount { get; }
        public long LoadRequestCount { get; }
        public long BackendRequestCount { get; }
        public long ReusedLeaseCount { get; }
        public long FailedRequestCount { get; }
        public long RejectedRequestCount { get; }
        public long ReleasedRequestCount { get; }
    }
}
