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

        /// <summary>
        /// Index of the hit closest to the origin along the travelled path, or -1 when nothing was
        /// hit. Mirrors TrajectoryTraceResult.NearestHitIndex in the float Core.
        /// </summary>
        public readonly int NearestHitIndex;

        public DeterministicTrajectoryTraceResult(
            TrajectoryTraceFlags flags,
            int segmentCount,
            int hitCount,
            FPInt64 travelDistance,
            FPVector3 endPosition,
            int nearestHitIndex = -1)
        {
            Flags = flags;
            SegmentCount = segmentCount;
            HitCount = hitCount;
            TravelDistance = travelDistance;
            EndPosition = endPosition;
            NearestHitIndex = nearestHitIndex;
        }
    }
}
