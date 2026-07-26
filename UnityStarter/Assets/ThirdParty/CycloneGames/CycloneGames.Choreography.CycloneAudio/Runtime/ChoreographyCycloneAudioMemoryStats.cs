namespace CycloneGames.Choreography.CycloneAudio
{
    /// <summary>Bridge-only counters. Audio clip bytes, bank memory, and backend voices remain owned by Audio.</summary>
    public readonly struct ChoreographyCycloneAudioMemoryStats
    {
        public ChoreographyCycloneAudioMemoryStats(
            int activeHandleCount,
            int maximumActiveHandleCount,
            int pendingRequestCount,
            int peakActiveHandleCount,
            int peakPendingRequestCount,
            long playbackRequestCount,
            long successfulRequestCount,
            long failedRequestCount,
            long rejectedRequestCount,
            long releasedHandleCount)
        {
            ActiveHandleCount = activeHandleCount;
            MaximumActiveHandleCount = maximumActiveHandleCount;
            PendingRequestCount = pendingRequestCount;
            PeakActiveHandleCount = peakActiveHandleCount;
            PeakPendingRequestCount = peakPendingRequestCount;
            PlaybackRequestCount = playbackRequestCount;
            SuccessfulRequestCount = successfulRequestCount;
            FailedRequestCount = failedRequestCount;
            RejectedRequestCount = rejectedRequestCount;
            ReleasedHandleCount = releasedHandleCount;
        }

        public int ActiveHandleCount { get; }
        public int MaximumActiveHandleCount { get; }
        public int PendingRequestCount { get; }
        public int PeakActiveHandleCount { get; }
        public int PeakPendingRequestCount { get; }
        public long PlaybackRequestCount { get; }
        public long SuccessfulRequestCount { get; }
        public long FailedRequestCount { get; }
        public long RejectedRequestCount { get; }
        public long ReleasedHandleCount { get; }
    }
}
