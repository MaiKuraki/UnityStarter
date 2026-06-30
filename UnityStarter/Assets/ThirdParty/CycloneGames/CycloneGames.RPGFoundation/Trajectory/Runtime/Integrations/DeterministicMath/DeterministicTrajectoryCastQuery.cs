using CycloneGames.DeterministicMath;

namespace CycloneGames.RPGFoundation.Trajectory.Integrations.DeterministicMath
{
    public readonly struct DeterministicTrajectoryCastQuery
    {
        public readonly int TraceId;
        public readonly int SegmentIndex;
        public readonly ulong OwnerEntityId;
        public readonly int CollisionLayerMask;
        public readonly FPInt64 Radius;
        public readonly FPInt64 Distance;
        public readonly FPVector3 From;
        public readonly FPVector3 To;
        public readonly FPVector3 Direction;
        public readonly ulong IgnoredTargetEntityId;
        public readonly int IgnoredTargetObjectId;

        public DeterministicTrajectoryCastQuery(
            int traceId,
            int segmentIndex,
            ulong ownerEntityId,
            int collisionLayerMask,
            FPInt64 radius,
            FPInt64 distance,
            FPVector3 from,
            FPVector3 to,
            FPVector3 direction,
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
