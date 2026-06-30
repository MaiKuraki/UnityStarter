using CycloneGames.DeterministicMath;

namespace CycloneGames.RPGFoundation.Trajectory.Integrations.DeterministicMath
{
    public readonly struct DeterministicTrajectoryQuery
    {
        public const int DEFAULT_MAX_ITERATION_COUNT = 16;

        public static readonly FPInt64 DefaultSurfaceOffset = FPInt64.FromFloat(0.001f);

        public readonly int TraceId;
        public readonly ulong OwnerEntityId;
        public readonly int CollisionLayerMask;
        public readonly FPVector3 Origin;
        public readonly FPVector3 Direction;
        public readonly FPInt64 MaxDistance;
        public readonly FPInt64 Radius;
        public readonly int MaxReflectionCount;
        public readonly int MaxPierceCount;
        public readonly int MaxHitCount;
        public readonly int MaxIterationCount;
        public readonly FPInt64 SurfaceOffset;
        public readonly ulong InitialIgnoredTargetEntityId;
        public readonly int InitialIgnoredTargetObjectId;

        public DeterministicTrajectoryQuery(
            int traceId,
            ulong ownerEntityId,
            int collisionLayerMask,
            FPVector3 origin,
            FPVector3 direction,
            FPInt64 maxDistance,
            FPInt64 radius,
            int maxReflectionCount,
            int maxPierceCount,
            int maxHitCount,
            int maxIterationCount,
            FPInt64 surfaceOffset,
            ulong initialIgnoredTargetEntityId = 0UL,
            int initialIgnoredTargetObjectId = 0)
        {
            TraceId = traceId;
            OwnerEntityId = ownerEntityId;
            CollisionLayerMask = collisionLayerMask;
            Origin = origin;
            Direction = direction;
            MaxDistance = maxDistance;
            Radius = radius;
            MaxReflectionCount = maxReflectionCount;
            MaxPierceCount = maxPierceCount;
            MaxHitCount = maxHitCount;
            MaxIterationCount = maxIterationCount;
            SurfaceOffset = surfaceOffset;
            InitialIgnoredTargetEntityId = initialIgnoredTargetEntityId;
            InitialIgnoredTargetObjectId = initialIgnoredTargetObjectId;
        }

        public bool IsValid
        {
            get
            {
                return CollisionLayerMask != 0
                    && Direction.SqrMagnitude.RawValue > 0
                    && MaxDistance.RawValue > 0
                    && Radius.RawValue >= 0
                    && MaxReflectionCount >= 0
                    && MaxPierceCount >= 0
                    && MaxHitCount > 0
                    && MaxIterationCount > 0
                    && SurfaceOffset.RawValue >= 0;
            }
        }

        public static DeterministicTrajectoryQuery CreateRay(
            int traceId,
            ulong ownerEntityId,
            int collisionLayerMask,
            FPVector3 origin,
            FPVector3 direction,
            FPInt64 maxDistance,
            int maxReflectionCount = 0)
        {
            return new DeterministicTrajectoryQuery(
                traceId,
                ownerEntityId,
                collisionLayerMask,
                origin,
                direction,
                maxDistance,
                FPInt64.Zero,
                maxReflectionCount,
                maxPierceCount: 0,
                maxHitCount: int.MaxValue,
                DEFAULT_MAX_ITERATION_COUNT,
                DefaultSurfaceOffset);
        }
    }
}
