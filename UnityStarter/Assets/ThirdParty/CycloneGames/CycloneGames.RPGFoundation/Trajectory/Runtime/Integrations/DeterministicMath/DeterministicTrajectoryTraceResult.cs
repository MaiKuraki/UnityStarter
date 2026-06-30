using CycloneGames.DeterministicMath;
using CycloneGames.RPGFoundation.Trajectory.Core;

namespace CycloneGames.RPGFoundation.Trajectory.Integrations.DeterministicMath
{
    public readonly struct DeterministicTrajectoryTraceResult
    {
        public readonly TrajectoryTraceFlags Flags;
        public readonly int SegmentCount;
        public readonly int HitCount;
        public readonly FPInt64 TravelDistance;
        public readonly FPVector3 EndPosition;

        public DeterministicTrajectoryTraceResult(
            TrajectoryTraceFlags flags,
            int segmentCount,
            int hitCount,
            FPInt64 travelDistance,
            FPVector3 endPosition)
        {
            Flags = flags;
            SegmentCount = segmentCount;
            HitCount = hitCount;
            TravelDistance = travelDistance;
            EndPosition = endPosition;
        }
    }
}
