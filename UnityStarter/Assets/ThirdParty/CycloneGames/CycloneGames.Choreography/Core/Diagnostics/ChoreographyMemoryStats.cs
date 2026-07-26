namespace CycloneGames.Choreography.Core
{
    /// <summary>Allocation-free snapshot of scheduler-owned retained containers and admission counters.</summary>
    public readonly struct ChoreographySchedulerMemoryStats
    {
        public ChoreographySchedulerMemoryStats(
            int activeCount,
            int maximumActiveCount,
            int peakActiveCount,
            int queuedCount,
            int maximumQueuedCount,
            int peakQueuedCount,
            int retainedInstanceCount,
            int retainedPlayerCount,
            int maximumRetainedPoolCount,
            int sampleBufferCapacity,
            int maximumSampleCount,
            int peakSampleCount,
            long rejectedActiveCount,
            long rejectedQueuedCount,
            long strategyRejectedCount,
            long trimmedPoolItemCount,
            long droppedSampleCount)
        {
            ActiveCount = activeCount;
            MaximumActiveCount = maximumActiveCount;
            PeakActiveCount = peakActiveCount;
            QueuedCount = queuedCount;
            MaximumQueuedCount = maximumQueuedCount;
            PeakQueuedCount = peakQueuedCount;
            RetainedInstanceCount = retainedInstanceCount;
            RetainedPlayerCount = retainedPlayerCount;
            MaximumRetainedPoolCount = maximumRetainedPoolCount;
            SampleBufferCapacity = sampleBufferCapacity;
            MaximumSampleCount = maximumSampleCount;
            PeakSampleCount = peakSampleCount;
            RejectedActiveCount = rejectedActiveCount;
            RejectedQueuedCount = rejectedQueuedCount;
            StrategyRejectedCount = strategyRejectedCount;
            TrimmedPoolItemCount = trimmedPoolItemCount;
            DroppedSampleCount = droppedSampleCount;
        }

        public int ActiveCount { get; }
        public int MaximumActiveCount { get; }
        public int PeakActiveCount { get; }
        public int QueuedCount { get; }
        public int MaximumQueuedCount { get; }
        public int PeakQueuedCount { get; }
        public int RetainedInstanceCount { get; }
        public int RetainedPlayerCount { get; }
        public int MaximumRetainedPoolCount { get; }
        public int SampleBufferCapacity { get; }
        public int MaximumSampleCount { get; }
        public int PeakSampleCount { get; }
        public long RejectedActiveCount { get; }
        public long RejectedQueuedCount { get; }
        public long StrategyRejectedCount { get; }
        public long TrimmedPoolItemCount { get; }
        public long DroppedSampleCount { get; }
    }

    /// <summary>Allocation-free snapshot of one preload runner's retained handles and bounded work.</summary>
    public readonly struct ChoreographyPreloadMemoryStats
    {
        public ChoreographyPreloadMemoryStats(
            int referenceCount,
            int maximumReferenceCount,
            int activeHandleCount,
            int completedHandleCount,
            int failedReferenceCount,
            int maximumConcurrentLoadCount,
            int peakActiveHandleCount,
            int peakRetainedHandleCount,
            long startedLoadCount,
            long succeededLoadCount,
            long failedLoadCount,
            long rejectedReferenceCount,
            long releasedHandleCount,
            long cancelledBatchCount)
        {
            ReferenceCount = referenceCount;
            MaximumReferenceCount = maximumReferenceCount;
            ActiveHandleCount = activeHandleCount;
            CompletedHandleCount = completedHandleCount;
            FailedReferenceCount = failedReferenceCount;
            MaximumConcurrentLoadCount = maximumConcurrentLoadCount;
            PeakActiveHandleCount = peakActiveHandleCount;
            PeakRetainedHandleCount = peakRetainedHandleCount;
            StartedLoadCount = startedLoadCount;
            SucceededLoadCount = succeededLoadCount;
            FailedLoadCount = failedLoadCount;
            RejectedReferenceCount = rejectedReferenceCount;
            ReleasedHandleCount = releasedHandleCount;
            CancelledBatchCount = cancelledBatchCount;
        }

        public int ReferenceCount { get; }
        public int MaximumReferenceCount { get; }
        public int ActiveHandleCount { get; }
        public int CompletedHandleCount { get; }
        public int FailedReferenceCount { get; }
        public int MaximumConcurrentLoadCount { get; }
        public int PeakActiveHandleCount { get; }
        public int PeakRetainedHandleCount { get; }
        public long StartedLoadCount { get; }
        public long SucceededLoadCount { get; }
        public long FailedLoadCount { get; }
        public long RejectedReferenceCount { get; }
        public long ReleasedHandleCount { get; }
        public long CancelledBatchCount { get; }
    }
}
