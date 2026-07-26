namespace CycloneGames.Networking.Security
{
    public readonly struct RateLimiterMemorySnapshot
    {
        internal RateLimiterMemorySnapshot(
            int trackedConnectionCount,
            int maximumTrackedConnections,
            int maximumMessagesPerSecond,
            long maximumBytesPerSecond,
            int burstLimit,
            long capacityRejectionCount,
            long expiredConnectionPruneCount)
        {
            TrackedConnectionCount = trackedConnectionCount;
            MaximumTrackedConnections = maximumTrackedConnections;
            MaximumMessagesPerSecond = maximumMessagesPerSecond;
            MaximumBytesPerSecond = maximumBytesPerSecond;
            BurstLimit = burstLimit;
            CapacityRejectionCount = capacityRejectionCount;
            ExpiredConnectionPruneCount = expiredConnectionPruneCount;
        }

        public int TrackedConnectionCount { get; }
        public int MaximumTrackedConnections { get; }
        public int MaximumMessagesPerSecond { get; }
        public long MaximumBytesPerSecond { get; }
        public int BurstLimit { get; }
        public long CapacityRejectionCount { get; }
        public long ExpiredConnectionPruneCount { get; }
    }
}
