namespace CycloneGames.RPGFoundation.Trajectory.Core
{
    public readonly struct TrajectoryQuery
    {
        public const float DEFAULT_SURFACE_OFFSET = 0.001f;
        public const int DEFAULT_MAX_ITERATION_COUNT = 16;

        public readonly int TraceId;
        public readonly ulong OwnerEntityId;
        public readonly int CollisionLayerMask;
        public readonly TrajectoryVector3 Origin;
        public readonly TrajectoryVector3 Direction;
        public readonly float MaxDistance;
        public readonly float Radius;
        public readonly int MaxReflectionCount;
        public readonly int MaxPierceCount;
        public readonly int MaxHitCount;
        public readonly int MaxIterationCount;
        public readonly float SurfaceOffset;
        public readonly ulong InitialIgnoredTargetEntityId;
        public readonly int InitialIgnoredTargetObjectId;

        public TrajectoryQuery(
            int traceId,
            ulong ownerEntityId,
            int collisionLayerMask,
            TrajectoryVector3 origin,
            TrajectoryVector3 direction,
            float maxDistance,
            float radius = 0f,
            int maxReflectionCount = 0,
            int maxPierceCount = 0,
            int maxHitCount = int.MaxValue,
            int maxIterationCount = DEFAULT_MAX_ITERATION_COUNT,
            float surfaceOffset = DEFAULT_SURFACE_OFFSET,
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
                    && Origin.IsFinite
                    && Direction.IsFinite
                    && Direction.LengthSquared > 0.000001f
                    && MaxDistance > 0f
                    && Radius >= 0f
                    && MaxReflectionCount >= 0
                    && MaxPierceCount >= 0
                    && MaxHitCount > 0
                    && MaxIterationCount > 0
                    && SurfaceOffset >= 0f
                    && IsFinite(MaxDistance)
                    && IsFinite(Radius)
                    && IsFinite(SurfaceOffset);
            }
        }

        public static TrajectoryQuery CreateRay(
            int traceId,
            ulong ownerEntityId,
            int collisionLayerMask,
            TrajectoryVector3 origin,
            TrajectoryVector3 direction,
            float maxDistance,
            int maxReflectionCount = 0)
        {
            return new TrajectoryQuery(
                traceId,
                ownerEntityId,
                collisionLayerMask,
                origin,
                direction,
                maxDistance,
                radius: 0f,
                maxReflectionCount: maxReflectionCount);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
