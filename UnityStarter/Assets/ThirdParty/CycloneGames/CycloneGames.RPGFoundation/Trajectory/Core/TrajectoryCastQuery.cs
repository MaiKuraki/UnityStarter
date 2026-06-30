namespace CycloneGames.RPGFoundation.Trajectory.Core
{
    public readonly struct TrajectoryCastQuery
    {
        public readonly int TraceId;
        public readonly int SegmentIndex;
        public readonly ulong OwnerEntityId;
        public readonly int CollisionLayerMask;
        public readonly float Radius;
        public readonly float Distance;
        public readonly TrajectoryVector3 From;
        public readonly TrajectoryVector3 To;
        public readonly TrajectoryVector3 Direction;
        public readonly ulong IgnoredTargetEntityId;
        public readonly int IgnoredTargetObjectId;

        public TrajectoryCastQuery(
            int traceId,
            int segmentIndex,
            ulong ownerEntityId,
            int collisionLayerMask,
            float radius,
            float distance,
            TrajectoryVector3 from,
            TrajectoryVector3 to,
            TrajectoryVector3 direction,
            ulong ignoredTargetEntityId,
            int ignoredTargetObjectId)
        {
            TraceId = traceId;
            SegmentIndex = segmentIndex;
            OwnerEntityId = ownerEntityId;
            CollisionLayerMask = collisionLayerMask;
            Radius = radius;
            Distance = distance;
            From = from;
            To = to;
            Direction = direction;
            IgnoredTargetEntityId = ignoredTargetEntityId;
            IgnoredTargetObjectId = ignoredTargetObjectId;
        }
    }
}
