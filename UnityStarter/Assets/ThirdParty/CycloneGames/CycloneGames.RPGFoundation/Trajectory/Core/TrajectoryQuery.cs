namespace CycloneGames.RPGFoundation.Trajectory.Core
{
    public readonly struct TrajectoryQuery
    {
        /// <summary>
        /// Compile-time constant used as a constructor default argument. Consumers inline the value,
        /// so changing it requires rebuilding every assembly that calls this constructor.
        /// </summary>
        public const float DEFAULT_SURFACE_OFFSET = 0.001f;

        /// <summary>
        /// Compile-time constant used as a constructor default argument. Consumers inline the value,
        /// so changing it requires rebuilding every assembly that calls this constructor.
        /// </summary>
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
        public readonly ulong InitialIgnoredTargetObjectId;
        public readonly TrajectoryShape Shape;
        public readonly float Height;
        public readonly TrajectoryVector3 HalfExtents;

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
            ulong initialIgnoredTargetObjectId = 0UL,
            TrajectoryShape shape = TrajectoryShape.Ray,
            float height = 0f,
            TrajectoryVector3 halfExtents = default)
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
            Shape = shape;
            Height = height;
            HalfExtents = halfExtents;
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
                    && IsFinite(SurfaceOffset)
                    && IsShapeValid();
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

        /// <summary>
        /// Builds a radius sweep query. A positive <paramref name="radius"/> maps to a sphere or
        /// circle cast in the Unity adapters; zero degenerates to a ray.
        /// </summary>
        public static TrajectoryQuery CreateSweep(
            int traceId,
            ulong ownerEntityId,
            int collisionLayerMask,
            TrajectoryVector3 origin,
            TrajectoryVector3 direction,
            float maxDistance,
            float radius,
            int maxReflectionCount = 0,
            int maxPierceCount = 0,
            int maxHitCount = int.MaxValue,
            int maxIterationCount = DEFAULT_MAX_ITERATION_COUNT,
            float surfaceOffset = DEFAULT_SURFACE_OFFSET)
        {
            return new TrajectoryQuery(
                traceId,
                ownerEntityId,
                collisionLayerMask,
                origin,
                direction,
                maxDistance,
                radius,
                maxReflectionCount,
                maxPierceCount,
                maxHitCount,
                maxIterationCount,
                surfaceOffset,
                shape: radius > 0f ? TrajectoryShape.Sphere : TrajectoryShape.Ray);
        }

        /// <summary>
        /// Builds a capsule sweep query. <paramref name="height"/> is the total capsule height
        /// including both hemispherical caps and must be at least <c>2 * radius</c>; the capsule is
        /// aligned to the world up axis so results stay reproducible across sweep directions.
        /// </summary>
        public static TrajectoryQuery CreateCapsule(
            int traceId,
            ulong ownerEntityId,
            int collisionLayerMask,
            TrajectoryVector3 origin,
            TrajectoryVector3 direction,
            float maxDistance,
            float radius,
            float height,
            int maxReflectionCount = 0,
            int maxPierceCount = 0,
            int maxHitCount = int.MaxValue,
            int maxIterationCount = DEFAULT_MAX_ITERATION_COUNT,
            float surfaceOffset = DEFAULT_SURFACE_OFFSET)
        {
            return new TrajectoryQuery(
                traceId,
                ownerEntityId,
                collisionLayerMask,
                origin,
                direction,
                maxDistance,
                radius,
                maxReflectionCount,
                maxPierceCount,
                maxHitCount,
                maxIterationCount,
                surfaceOffset,
                shape: TrajectoryShape.Capsule,
                height: height);
        }

        /// <summary>
        /// Builds an axis-aligned box sweep query. Axis alignment is deliberate: orienting the box
        /// along the sweep direction makes results depend on travel direction and breaks replay.
        /// </summary>
        public static TrajectoryQuery CreateBox(
            int traceId,
            ulong ownerEntityId,
            int collisionLayerMask,
            TrajectoryVector3 origin,
            TrajectoryVector3 direction,
            float maxDistance,
            TrajectoryVector3 halfExtents,
            int maxReflectionCount = 0,
            int maxPierceCount = 0,
            int maxHitCount = int.MaxValue,
            int maxIterationCount = DEFAULT_MAX_ITERATION_COUNT,
            float surfaceOffset = DEFAULT_SURFACE_OFFSET)
        {
            return new TrajectoryQuery(
                traceId,
                ownerEntityId,
                collisionLayerMask,
                origin,
                direction,
                maxDistance,
                0f,
                maxReflectionCount,
                maxPierceCount,
                maxHitCount,
                maxIterationCount,
                surfaceOffset,
                shape: TrajectoryShape.Box,
                halfExtents: halfExtents);
        }

        private bool IsShapeValid()
        {
            switch (Shape)
            {
                case TrajectoryShape.Ray:
                    return true;
                case TrajectoryShape.Sphere:
                    return Radius > 0f;
                case TrajectoryShape.Capsule:
                    return Radius > 0f && IsFinite(Height) && Height >= Radius * 2f;
                case TrajectoryShape.Box:
                    return IsFinite(HalfExtents.X) && IsFinite(HalfExtents.Y) && IsFinite(HalfExtents.Z)
                        && HalfExtents.X > 0f && HalfExtents.Y > 0f && HalfExtents.Z > 0f;
                default:
                    return false;
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
