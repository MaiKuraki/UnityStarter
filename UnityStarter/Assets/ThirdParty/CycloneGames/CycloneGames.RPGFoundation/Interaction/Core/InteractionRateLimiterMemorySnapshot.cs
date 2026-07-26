namespace CycloneGames.RPGFoundation.Interaction.Core
{
    /// <summary>Allocation-free diagnostics for one InteractionRateLimiter.</summary>
    public readonly struct InteractionRateLimiterMemorySnapshot
    {
        internal InteractionRateLimiterMemorySnapshot(
            int windowCount,
            int windowCapacity,
            long rejectedAdmissionCount)
            : this(windowCount, windowCapacity, rejectedAdmissionCount, 0L, 0L)
        {
        }

        internal InteractionRateLimiterMemorySnapshot(
            int windowCount,
            int windowCapacity,
            long rejectedAdmissionCount,
            long expiredWindowRemovalCount,
            long explicitWindowRemovalCount)
        {
            WindowCount = windowCount;
            WindowCapacity = windowCapacity;
            RejectedAdmissionCount = rejectedAdmissionCount;
            ExpiredWindowRemovalCount = expiredWindowRemovalCount;
            ExplicitWindowRemovalCount = explicitWindowRemovalCount;
        }

        public int WindowCount { get; }
        public int WindowCapacity { get; }
        public long RejectedAdmissionCount { get; }
        public long ExpiredWindowRemovalCount { get; }
        public long ExplicitWindowRemovalCount { get; }
    }
}
