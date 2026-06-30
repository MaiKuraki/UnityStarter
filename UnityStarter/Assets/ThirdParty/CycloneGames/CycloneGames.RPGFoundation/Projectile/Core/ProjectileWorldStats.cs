namespace CycloneGames.RPGFoundation.Projectile.Core
{
    public readonly struct ProjectileWorldStats
    {
        public readonly int Capacity;
        public readonly int ActiveCount;
        public readonly int PeakActiveCount;
        public readonly int StepCount;
        public readonly int LastTick;
        public readonly float LastDeltaTime;
        public readonly int LastHitEventCount;
        public readonly int LastCollisionQueryCount;
        public readonly int LastCollisionHitCount;
        public readonly int TotalSpawnAcceptedCount;
        public readonly int TotalSpawnRejectedInvalidCount;
        public readonly int TotalSpawnRejectedCapacityCount;
        public readonly int TotalDespawnCount;
        public readonly int TotalHitEventCount;
        public readonly int TotalHitEventOverflowCount;
        public readonly int TotalCollisionIterationLimitCount;

        public ProjectileWorldStats(
            int capacity,
            int activeCount,
            int peakActiveCount,
            int stepCount,
            int lastTick,
            float lastDeltaTime,
            int lastHitEventCount,
            int lastCollisionQueryCount,
            int lastCollisionHitCount,
            int totalSpawnAcceptedCount,
            int totalSpawnRejectedInvalidCount,
            int totalSpawnRejectedCapacityCount,
            int totalDespawnCount,
            int totalHitEventCount,
            int totalHitEventOverflowCount,
            int totalCollisionIterationLimitCount)
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
