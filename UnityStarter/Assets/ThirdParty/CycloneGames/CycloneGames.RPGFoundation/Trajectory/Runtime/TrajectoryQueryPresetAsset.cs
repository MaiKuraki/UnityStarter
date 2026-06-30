using System;
using CycloneGames.RPGFoundation.Trajectory.Core;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Trajectory.Runtime
{
    [CreateAssetMenu(
        fileName = "TrajectoryQueryPreset",
        menuName = "CycloneGames/RPGFoundation/Trajectory/Query Preset")]
    public class TrajectoryQueryPresetAsset : ScriptableObject
    {
        [SerializeField] private LayerMask CollisionLayerMask = ~0;
        [SerializeField] private float MaxDistance = 40f;
        [SerializeField] private float Radius;
        [SerializeField] private int MaxReflectionCount;
        [SerializeField] private int MaxPierceCount;
        [SerializeField] private int MaxHitCount = 8;
        [SerializeField] private int MaxIterationCount = TrajectoryQuery.DEFAULT_MAX_ITERATION_COUNT;
        [SerializeField] private float SurfaceOffset = TrajectoryQuery.DEFAULT_SURFACE_OFFSET;
        [SerializeField] private ulong InitialIgnoredTargetEntityId;
        [SerializeField] private int InitialIgnoredTargetObjectId;

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
                InitialIgnoredTargetObjectId);
        }

        protected static TrajectoryVector3 ToTrajectoryVector3(Vector3 value)
        {
            return new TrajectoryVector3(value.x, value.y, value.z);
        }
    }
}
