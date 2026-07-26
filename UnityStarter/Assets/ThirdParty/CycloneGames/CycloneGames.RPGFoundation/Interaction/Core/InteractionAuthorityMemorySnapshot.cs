namespace CycloneGames.RPGFoundation.Interaction.Core
{
    /// <summary>Allocation-free aggregate diagnostics for one InteractionAuthorityService.</summary>
    public readonly struct InteractionAuthorityMemorySnapshot
    {
        internal InteractionAuthorityMemorySnapshot(
            int registeredTargetCount,
            int queueOwnerCount,
            int requestHistoryCount,
            int rateLimitWindowCount,
            int registeredTargetCapacity,
            int queueOwnerCapacity,
            int rateLimitWindowCapacity,
            long rejectedTargetAdmissionCount,
            long rejectedQueueOwnerAdmissionCount,
            long rejectedRateLimitWindowAdmissionCount,
            InteractionAuthorityOptions options,
            InteractionMetricsSnapshot metrics)
        {
            RegisteredTargetCount = registeredTargetCount;
            QueueOwnerCount = queueOwnerCount;
            RequestHistoryCount = requestHistoryCount;
            RateLimitWindowCount = rateLimitWindowCount;
            RegisteredTargetCapacity = registeredTargetCapacity;
            QueueOwnerCapacity = queueOwnerCapacity;
            RateLimitWindowCapacity = rateLimitWindowCapacity;
            RejectedTargetAdmissionCount = rejectedTargetAdmissionCount;
            RejectedQueueOwnerAdmissionCount = rejectedQueueOwnerAdmissionCount;
            RejectedRateLimitWindowAdmissionCount = rejectedRateLimitWindowAdmissionCount;
            Options = options;
            Metrics = metrics;
        }

        public int RegisteredTargetCount { get; }
        public int QueueOwnerCount { get; }
        public int RequestHistoryCount { get; }
        public int RateLimitWindowCount { get; }
        public int RegisteredTargetCapacity { get; }
        public int QueueOwnerCapacity { get; }
        public int RateLimitWindowCapacity { get; }
        public long RejectedTargetAdmissionCount { get; }
        public long RejectedQueueOwnerAdmissionCount { get; }
        public long RejectedRateLimitWindowAdmissionCount { get; }
        public InteractionAuthorityOptions Options { get; }
        public InteractionMetricsSnapshot Metrics { get; }
    }
}
