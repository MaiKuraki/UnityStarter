using CycloneGames.DeterministicMath;
using CycloneGames.RPGFoundation.Trajectory.Core;

namespace CycloneGames.RPGFoundation.Trajectory.Integrations.DeterministicMath
{
    public readonly struct DeterministicTrajectoryHit
    {
        public readonly ulong TargetEntityId;
        public readonly int TargetObjectId;
        public readonly int HitLayerMask;
        public readonly FPInt64 Distance;
        public readonly int SegmentIndex;
        public readonly TrajectoryHitResponse Response;
        public readonly FPVector3 Position;
        public readonly FPVector3 Normal;

        public DeterministicTrajectoryHit(
            ulong targetEntityId,
            int targetObjectId,
            int hitLayerMask,
            FPInt64 distance,
            FPVector3 position,
            FPVector3 normal,
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
                return Distance.RawValue >= 0
                    && (TargetEntityId != 0UL || TargetObjectId != 0 || HitLayerMask != 0);
            }
        }
    }
}
