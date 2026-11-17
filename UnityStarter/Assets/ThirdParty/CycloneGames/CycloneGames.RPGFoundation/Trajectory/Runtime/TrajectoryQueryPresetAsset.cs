using System;
using CycloneGames.RPGFoundation.Trajectory.Core;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Trajectory.Runtime
{
    [CreateAssetMenu(
        fileName = "TrajectoryQueryPreset",
        menuName = "CycloneGames/RPG Foundation/Trajectory/Query Preset")]
    public class TrajectoryQueryPresetAsset : ScriptableObject
    {
        [SerializeField] private LayerMask CollisionLayerMask = ~0;
        [SerializeField] private float MaxDistance = 40f;
        [SerializeField] private TrajectoryShape Shape = TrajectoryShape.Ray;
        [SerializeField] private float Radius;
        [SerializeField] private float Height = 1.8f;
        [SerializeField] private Vector3 HalfExtents = new Vector3(0.5f, 0.5f, 0.5f);
        [SerializeField] private int MaxReflectionCount;
        [SerializeField] private int MaxPierceCount;
        [SerializeField] private int MaxHitCount = 8;
        [SerializeField] private int MaxIterationCount = TrajectoryQuery.DEFAULT_MAX_ITERATION_COUNT;
        [SerializeField] private float SurfaceOffset = TrajectoryQuery.DEFAULT_SURFACE_OFFSET;
        [SerializeField] private ulong InitialIgnoredTargetEntityId;
        [SerializeField] private ulong InitialIgnoredTargetObjectId;

        public int CollisionMask
        {
            get
            {
                return CollisionLayerMask.value;
            }
        }

        public float Distance
        {
            get
            {
                return MaxDistance;
            }
        }

        /// <summary>Sweep volume authored by this preset.</summary>
        public TrajectoryShape QueryShape
        {
            get
            {
                return Shape;
            }
        }

        /// <summary>Sphere or capsule radius, zero for ray and box presets.</summary>
        public float RadiusValue
        {
            get
            {
                return Radius;
            }
        }

        /// <summary>Box half extents as a solver-space vector.</summary>
        public TrajectoryVector3 HalfExtentsValue
        {
            get
            {
                return new TrajectoryVector3(HalfExtents.x, HalfExtents.y, HalfExtents.z);
            }
        }

        /// <summary>Total capsule height including both caps.</summary>
        public float HeightValue
        {
            get
            {
                return Height;
            }
        }

        public virtual TrajectoryQuery BuildQuery(
            int traceId,
            ulong ownerEntityId,
            Vector3 origin,
            Vector3 direction)
        {
            return CreateQuery(traceId, ownerEntityId, origin, direction, sanitize: true);
        }

        public virtual TrajectoryQuery BuildAuthoringQuery(
            int traceId,
            ulong ownerEntityId,
            Vector3 origin,
            Vector3 direction)
        {
            return CreateQuery(traceId, ownerEntityId, origin, direction, sanitize: false);
        }

        private TrajectoryQuery CreateQuery(
            int traceId,
            ulong ownerEntityId,
            Vector3 origin,
            Vector3 direction,
            bool sanitize)
        {
            return new TrajectoryQuery(
                traceId,
                ownerEntityId,
                CollisionLayerMask.value,
                ToTrajectoryVector3(origin),
                ToTrajectoryVector3(direction),
                sanitize ? Math.Max(0f, MaxDistance) : MaxDistance,
                sanitize ? Math.Max(0f, Radius) : Radius,
                sanitize ? Math.Max(0, MaxReflectionCount) : MaxReflectionCount,
                sanitize ? Math.Max(0, MaxPierceCount) : MaxPierceCount,
                sanitize ? Math.Max(1, MaxHitCount) : MaxHitCount,
                sanitize ? Math.Max(1, MaxIterationCount) : MaxIterationCount,
                sanitize ? Math.Max(0f, SurfaceOffset) : SurfaceOffset,
                InitialIgnoredTargetEntityId,
                InitialIgnoredTargetObjectId,
                Shape,
                sanitize ? Math.Max(0f, Height) : Height,
                new TrajectoryVector3(HalfExtents.x, HalfExtents.y, HalfExtents.z));
        }

        protected static TrajectoryVector3 ToTrajectoryVector3(Vector3 value)
        {
            return new TrajectoryVector3(value.x, value.y, value.z);
        }
    }
}
