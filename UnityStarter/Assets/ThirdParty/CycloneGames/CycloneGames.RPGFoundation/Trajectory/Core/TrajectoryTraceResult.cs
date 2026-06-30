namespace CycloneGames.RPGFoundation.Trajectory.Core
{
    public readonly struct TrajectoryTraceResult
    {
        public readonly TrajectoryTraceFlags Flags;
        public readonly int SegmentCount;
        public readonly int HitCount;
        public readonly float TravelDistance;
        public readonly TrajectoryVector3 EndPosition;

        public TrajectoryTraceResult(
            TrajectoryTraceFlags flags,
            int segmentCount,
            int hitCount,
            float travelDistance,
            TrajectoryVector3 endPosition)
        {
            Flags = flags;
            SegmentCount = segmentCount;
            HitCount = hitCount;
            TravelDistance = travelDistance;
            EndPosition = endPosition;
        }

        public bool IsComplete
        {
            get
            {
                return Flags == TrajectoryTraceFlags.None;
            }
        }

        public bool HasHit
        {
            get
            {
                return HitCount > 0;
            }
        }
    }
}
