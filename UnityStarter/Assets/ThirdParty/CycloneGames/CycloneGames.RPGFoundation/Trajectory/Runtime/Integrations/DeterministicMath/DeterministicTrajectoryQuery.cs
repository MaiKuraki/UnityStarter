using CycloneGames.DeterministicMath;
using CycloneGames.RPGFoundation.Trajectory.Core;

namespace CycloneGames.RPGFoundation.Trajectory.Integrations.DeterministicMath
{
    /// <summary>
    /// Fixed-point counterpart of <see cref="TrajectoryQuery"/>. Shape fields mirror the float
    /// model so the same four sweep volumes can be authored for deterministic replay.
    /// </summary>
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
        public readonly ulong InitialIgnoredTargetObjectId;
        public readonly TrajectoryShape Shape;
        public readonly FPInt64 Height;
        public readonly FPVector3 HalfExtents;

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
            ulong initialIgnoredTargetObjectId = 0UL,
            TrajectoryShape shape = TrajectoryShape.Ray,
            FPInt64 height = default,
            FPVector3 halfExtents = default)
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
                    && Direction.SqrMagnitude.RawValue > 0
                    && MaxDistance.RawValue > 0
                    && Radius.RawValue >= 0
                    && MaxReflectionCount >= 0
                    && MaxPierceCount >= 0
                    && MaxHitCount > 0
                    && MaxIterationCount > 0
                    && SurfaceOffset.RawValue >= 0
                    && HasValidShape();
            }
        }

        private bool HasValidShape()
        {
            switch (Shape)
            {
                case TrajectoryShape.Ray:
                    return true;
                case TrajectoryShape.Sphere:
                    return Radius.RawValue > 0;
                case TrajectoryShape.Capsule:
                    return Radius.RawValue > 0 && Height >= Radius * FPInt64.FromInt(2);
                case TrajectoryShape.Box:
                    return HalfExtents.X.RawValue > 0
                        && HalfExtents.Y.RawValue > 0
                        && HalfExtents.Z.RawValue > 0;
                default:
                    return false;
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
                DefaultSurfaceOffset,
                shape: TrajectoryShape.Ray);
        }

        /// <summary>
        /// Fixed-point counterpart of <see cref="TrajectoryQuery.CreateSweep"/>.
        /// Keeps the two query models constructible through the same shape.
        /// </summary>
        public static DeterministicTrajectoryQuery CreateSweep(
            int traceId,
            ulong ownerEntityId,
            int collisionLayerMask,
            FPVector3 origin,
            FPVector3 direction,
            FPInt64 maxDistance,
            FPInt64 radius,
            int maxReflectionCount = 0,
            int maxPierceCount = 0,
            int maxHitCount = int.MaxValue,
            int maxIterationCount = DEFAULT_MAX_ITERATION_COUNT,
            FPInt64? surfaceOffset = null)
        {
            return new DeterministicTrajectoryQuery(
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
                surfaceOffset.HasValue ? surfaceOffset.Value : DefaultSurfaceOffset,
                shape: TrajectoryShape.Sphere);
        }

        /// <summary>
        /// Capsule sweep aligned to world up, with total <paramref name="height"/> including both
        /// hemispherical caps. Height must be at least 2 * radius.
        /// </summary>
        public static DeterministicTrajectoryQuery CreateCapsule(
            int traceId,
            ulong ownerEntityId,
            int collisionLayerMask,
            FPVector3 origin,
            FPVector3 direction,
            FPInt64 maxDistance,
            FPInt64 radius,
            FPInt64 height,
            int maxReflectionCount = 0,
            int maxPierceCount = 0,
            int maxHitCount = int.MaxValue,
            int maxIterationCount = DEFAULT_MAX_ITERATION_COUNT,
            FPInt64? surfaceOffset = null)
        {
            return new DeterministicTrajectoryQuery(
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
                surfaceOffset.HasValue ? surfaceOffset.Value : DefaultSurfaceOffset,
                shape: TrajectoryShape.Capsule,
                height: height);
        }

        /// <summary>Axis-aligned box sweep. Axis alignment keeps replay independent of direction.</summary>
        public static DeterministicTrajectoryQuery CreateBox(
            int traceId,
            ulong ownerEntityId,
            int collisionLayerMask,
            FPVector3 origin,
            FPVector3 direction,
            FPInt64 maxDistance,
            FPVector3 halfExtents,
            int maxReflectionCount = 0,
            int maxPierceCount = 0,
            int maxHitCount = int.MaxValue,
            int maxIterationCount = DEFAULT_MAX_ITERATION_COUNT,
            FPInt64? surfaceOffset = null)
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
                maxPierceCount,
                maxHitCount,
                maxIterationCount,
                surfaceOffset.HasValue ? surfaceOffset.Value : DefaultSurfaceOffset,
                shape: TrajectoryShape.Box,
                halfExtents: halfExtents);
        }
    }
}
