namespace CycloneGames.RPGFoundation.Trajectory.Core
{
    public readonly struct TrajectoryHit
    {
        public readonly ulong TargetEntityId;
        public readonly int TargetObjectId;
        public readonly int HitLayerMask;
        public readonly float Distance;
        public readonly int SegmentIndex;
        public readonly TrajectoryHitResponse Response;
        public readonly TrajectoryVector3 Position;
        public readonly TrajectoryVector3 Normal;

        public TrajectoryHit(
            ulong targetEntityId,
            int targetObjectId,
            int hitLayerMask,
            float distance,
            TrajectoryVector3 position,
            TrajectoryVector3 normal,
            TrajectoryHitResponse response = TrajectoryHitResponse.Stop,
            int segmentIndex = -1)
        {
            TargetEntityId = targetEntityId;
            TargetObjectId = targetObjectId;
            HitLayerMask = hitLayerMask;
            Distance = distance;
            Position = position;
            Normal = normal;
            Response = response;
            SegmentIndex = segmentIndex;
        }

        public bool IsValid
        {
            get
            {
                return Distance >= 0f
                    && IsFinite(Distance)
                    && Position.IsFinite
                    && Normal.IsFinite
                    && (TargetEntityId != 0UL || TargetObjectId != 0 || HitLayerMask != 0);
            }
        }

        public TrajectoryHit WithSegmentIndex(int segmentIndex)
        {
            return new TrajectoryHit(
                TargetEntityId,
                TargetObjectId,
                HitLayerMask,
                Distance,
                Position,
                Normal,
                Response,
                segmentIndex);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
