namespace CycloneGames.RPGFoundation.Trajectory.Core
{
    public readonly struct TrajectoryTraceResult
    {
        public readonly TrajectoryTraceFlags Flags;
        public readonly int SegmentCount;
        public readonly int HitCount;
        public readonly float TravelDistance;
        public readonly TrajectoryVector3 EndPosition;

        /// <summary>
        /// Index of the hit closest to the origin along the travelled path, or -1 when nothing was
        /// hit. Saves callers a scan of the buffer, whose hits are stored in path order rather than
        /// distance order.
        /// </summary>
        public readonly int NearestHitIndex;

        public TrajectoryTraceResult(
            TrajectoryTraceFlags flags,
            int segmentCount,
            int hitCount,
            float travelDistance,
            TrajectoryVector3 endPosition,
            int nearestHitIndex = -1)
        {
            Flags = flags;
            SegmentCount = segmentCount;
            HitCount = hitCount;
            TravelDistance = travelDistance;
            EndPosition = endPosition;
            NearestHitIndex = nearestHitIndex;
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
