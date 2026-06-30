using System;

namespace CycloneGames.RPGFoundation.Trajectory.Core
{
    [Flags]
    public enum TrajectoryTraceFlags
    {
        None = 0,
        InvalidQuery = 1 << 0,
        MissingCollisionWorld = 1 << 1,
        HitCapacityReached = 1 << 2,
        SegmentCapacityReached = 1 << 3,
        IterationLimitReached = 1 << 4,
        MaxHitCountReached = 1 << 5,
        DegenerateDirection = 1 << 6
    }
}
