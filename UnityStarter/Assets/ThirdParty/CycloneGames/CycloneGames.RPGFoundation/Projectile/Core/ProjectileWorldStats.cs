namespace CycloneGames.RPGFoundation.Projectile.Core
{
    /// <summary>
    /// Observation snapshot. Lifetime counters are 64-bit so long-running servers never
    /// wrap into negative diagnostics.
    /// </summary>
    public readonly struct ProjectileWorldStats
    {
        public readonly int Capacity;
        public readonly int ActiveCount;
        public readonly int PeakActiveCount;
        public readonly long StepCount;
        public readonly int LastTick;
        public readonly float LastDeltaTime;
        public readonly long LastHitEventCount;
        public readonly long LastCollisionQueryCount;
        public readonly long LastCollisionHitCount;
        public readonly long TotalSpawnAcceptedCount;
        public readonly long TotalSpawnRejectedInvalidCount;
        public readonly long TotalSpawnRejectedCapacityCount;
        public readonly long TotalDespawnCount;
        public readonly long TotalHitEventCount;
        public readonly long TotalHitEventOverflowCount;
        public readonly long TotalCollisionIterationLimitCount;
        public readonly long TotalSubStepCount;
        public readonly long TotalClampedFrameCount;

        public ProjectileWorldStats(
            int capacity,
            int activeCount,
            int peakActiveCount,
            long stepCount,
            int lastTick,
            float lastDeltaTime,
            long lastHitEventCount,
            long lastCollisionQueryCount,
            long lastCollisionHitCount,
            long totalSpawnAcceptedCount,
            long totalSpawnRejectedInvalidCount,
            long totalSpawnRejectedCapacityCount,
            long totalDespawnCount,
            long totalHitEventCount,
            long totalHitEventOverflowCount,
            long totalCollisionIterationLimitCount,
            long totalSubStepCount,
            long totalClampedFrameCount)
        {
            Capacity = capacity;
            ActiveCount = activeCount;
            PeakActiveCount = peakActiveCount;
            StepCount = stepCount;
            LastTick = lastTick;
            LastDeltaTime = lastDeltaTime;
            LastHitEventCount = lastHitEventCount;
            LastCollisionQueryCount = lastCollisionQueryCount;
            LastCollisionHitCount = lastCollisionHitCount;
            TotalSpawnAcceptedCount = totalSpawnAcceptedCount;
            TotalSpawnRejectedInvalidCount = totalSpawnRejectedInvalidCount;
            TotalSpawnRejectedCapacityCount = totalSpawnRejectedCapacityCount;
            TotalDespawnCount = totalDespawnCount;
            TotalHitEventCount = totalHitEventCount;
            TotalHitEventOverflowCount = totalHitEventOverflowCount;
            TotalCollisionIterationLimitCount = totalCollisionIterationLimitCount;
            TotalSubStepCount = totalSubStepCount;
            TotalClampedFrameCount = totalClampedFrameCount;
        }

        public float ActiveCapacityRatio
        {
            get
            {
                return Capacity > 0 ? (float)ActiveCount / Capacity : 0f;
            }
        }
    }
}
