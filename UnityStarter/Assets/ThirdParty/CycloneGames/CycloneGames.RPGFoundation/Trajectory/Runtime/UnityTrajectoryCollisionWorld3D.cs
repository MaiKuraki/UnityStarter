using System;
using CycloneGames.RPGFoundation.Trajectory.Core;
using UnityEngine;
using CycloneGames.Compat;

namespace CycloneGames.RPGFoundation.Trajectory.Runtime
{
    public sealed class UnityTrajectoryCollisionWorld3D : ITrajectoryCollisionWorld
    {
        private const float EPSILON = 0.000001f;

        private readonly RaycastHit[] _hits;
        private readonly int _reflectionLayerMask;
        private readonly int _pierceLayerMask;
        private readonly QueryTriggerInteraction _queryTriggerInteraction;

        public UnityTrajectoryCollisionWorld3D(
            int capacity,
            int reflectionLayerMask = 0,
            int pierceLayerMask = 0,
            QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.Ignore)
        {
            _hits = new RaycastHit[Math.Max(1, capacity)];
            _reflectionLayerMask = reflectionLayerMask;
            _pierceLayerMask = pierceLayerMask;
            _queryTriggerInteraction = queryTriggerInteraction;
        }

        public int Cast(
            in TrajectoryCastQuery query,
            Span<TrajectoryHit> results)
        {
            if (results.Length == 0 || query.Distance <= EPSILON)
            {
                return 0;
            }

            Vector3 origin = ToVector3(query.From);
            Vector3 direction = ToVector3(query.Direction);
            if (direction.sqrMagnitude <= EPSILON)
            {
                return 0;
            }

            direction.Normalize();

            int hitCount;
            switch (query.Shape)
            {
                case TrajectoryShape.Sphere:
                    hitCount = query.Radius > EPSILON
                        ? Physics.SphereCastNonAlloc(origin, query.Radius, direction, _hits, query.Distance, query.CollisionLayerMask, _queryTriggerInteraction)
                        : Physics.RaycastNonAlloc(origin, direction, _hits, query.Distance, query.CollisionLayerMask, _queryTriggerInteraction);
                    break;

                case TrajectoryShape.Capsule:
                {
                    // CapsuleCast wants the two cap centres. Total height includes both hemispheres,
                    // so the straight section is Height - 2 * Radius. World-up alignment keeps the
                    // result independent of travel direction.
                    float halfSegment = query.Height * 0.5f - query.Radius;
                    if (halfSegment < 0f)
                    {
                        halfSegment = 0f;
                    }

                    Vector3 up = Vector3.up * halfSegment;
                    hitCount = Physics.CapsuleCastNonAlloc(
                        origin + up,
                        origin - up,
                        query.Radius,
                        direction,
                        _hits,
                        query.Distance,
                        query.CollisionLayerMask,
                        _queryTriggerInteraction);
                    break;
                }

                case TrajectoryShape.Box:
                    // Orientation is identity on purpose: an oriented box would make the result
                    // depend on the sweep direction and break replay determinism.
                    hitCount = Physics.BoxCastNonAlloc(
                        origin,
                        ToVector3(query.HalfExtents),
                        direction,
                        _hits,
                        Quaternion.identity,
                        query.Distance,
                        query.CollisionLayerMask,
                        _queryTriggerInteraction);
                    break;

                default:
                    hitCount = Physics.RaycastNonAlloc(origin, direction, _hits, query.Distance, query.CollisionLayerMask, _queryTriggerInteraction);
                    break;
            }

            int written = 0;
            int limit = Math.Min(Math.Min(hitCount, _hits.Length), results.Length);
            for (int i = 0; i < limit; i++)
            {
                RaycastHit hit = _hits[i];
                if (hit.collider == null)
                {
                    continue;
                }

                ulong objectId = UnityObjectId.Of(hit.collider);
                // Only the object channel is meaningful here: Unity colliders carry no entity id, so
                // every hit below reports TargetEntityId = 0. IgnoredTargetEntityId exists for
                // ECS/server adapters that do have one. Pierce repeats are suppressed by the solver
                // itself, so an adapter that skips this check is still correct.
                if (objectId == query.IgnoredTargetObjectId)
                {
                    continue;
                }

                int hitLayerMask = 1 << hit.collider.gameObject.layer;
                results[written] = new TrajectoryHit(
                    targetEntityId: 0UL,
                    targetObjectId: objectId,
                    hitLayerMask: hitLayerMask,
                    distance: hit.distance,
                    position: ToTrajectoryVector3(hit.point),
                    normal: ToTrajectoryVector3(hit.normal),
                    response: ResolveResponse(hitLayerMask),
                    segmentIndex: query.SegmentIndex);
                written++;
            }

            return written;
        }

        private TrajectoryHitResponse ResolveResponse(int hitLayerMask)
        {
            if ((hitLayerMask & _reflectionLayerMask) != 0)
            {
                return TrajectoryHitResponse.Reflect;
            }

            return (hitLayerMask & _pierceLayerMask) != 0
                ? TrajectoryHitResponse.Pierce
                : TrajectoryHitResponse.Stop;
        }

        private static Vector3 ToVector3(TrajectoryVector3 value)
        {
            return new Vector3(value.X, value.Y, value.Z);
        }

        private static TrajectoryVector3 ToTrajectoryVector3(Vector3 value)
        {
            return new TrajectoryVector3(value.x, value.y, value.z);
        }
    }
}
