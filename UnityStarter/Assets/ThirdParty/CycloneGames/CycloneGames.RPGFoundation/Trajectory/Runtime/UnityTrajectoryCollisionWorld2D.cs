using System;
using CycloneGames.RPGFoundation.Trajectory.Core;
using UnityEngine;
using CycloneGames.Compat;

namespace CycloneGames.RPGFoundation.Trajectory.Runtime
{
    public sealed class UnityTrajectoryCollisionWorld2D : ITrajectoryCollisionWorld
    {
        private const float EPSILON = 0.000001f;

        private readonly RaycastHit2D[] _hits;
        private readonly int _reflectionLayerMask;
        private readonly int _pierceLayerMask;
        private ContactFilter2D _filter;

        public UnityTrajectoryCollisionWorld2D(
            int capacity,
            int reflectionLayerMask = 0,
            int pierceLayerMask = 0,
            float minDepth = float.NegativeInfinity,
            float maxDepth = float.PositiveInfinity)
        {
            _hits = new RaycastHit2D[Math.Max(1, capacity)];
            _reflectionLayerMask = reflectionLayerMask;
            _pierceLayerMask = pierceLayerMask;

            // The filter is rebuilt per cast only for the layer mask; trigger, depth and layer filtering
            // are fixed for the lifetime of the world. useTriggers mirrors Physics2D.queriesHitTriggers,
            // which honoured Physics2D.queriesHitTriggers instead of always including triggers.
            _filter = new ContactFilter2D
            {
                useTriggers = Physics2D.queriesHitTriggers,
                useLayerMask = true,
                layerMask = 0
            };

            if (!float.IsNegativeInfinity(minDepth) || !float.IsPositiveInfinity(maxDepth))
            {
                _filter.useDepth = true;
                _filter.minDepth = minDepth;
                _filter.maxDepth = maxDepth;
            }
        }

        public int Cast(
            in TrajectoryCastQuery query,
            Span<TrajectoryHit> results)
        {
            if (results.Length == 0 || query.Distance <= EPSILON)
            {
                return 0;
            }

            Vector2 origin = new Vector2(query.From.X, query.From.Y);
            Vector2 direction = new Vector2(query.Direction.X, query.Direction.Y);
            if (direction.sqrMagnitude <= EPSILON)
            {
                return 0;
            }

            direction.Normalize();
            _filter.layerMask = query.CollisionLayerMask;

            int hitCount;
            switch (query.Shape)
            {
                case TrajectoryShape.Sphere:
                    hitCount = query.Radius > EPSILON
                        ? Physics2D.CircleCast(origin, query.Radius, direction, _filter, _hits, query.Distance)
                        : Physics2D.Raycast(origin, direction, _filter, _hits, query.Distance);
                    break;

                case TrajectoryShape.Capsule:
                    // Physics2D sizes are full extents, not radii or half extents.
                    hitCount = Physics2D.CapsuleCast(
                        origin,
                        new Vector2(query.Radius * 2f, query.Height),
                        CapsuleDirection2D.Vertical,
                        0f,
                        direction,
                        _filter,
                        _hits,
                        query.Distance);
                    break;

                case TrajectoryShape.Box:
                    hitCount = Physics2D.BoxCast(
                        origin,
                        new Vector2(query.HalfExtents.X * 2f, query.HalfExtents.Y * 2f),
                        0f,
                        direction,
                        _filter,
                        _hits,
                        query.Distance);
                    break;

                default:
                    hitCount = Physics2D.Raycast(origin, direction, _filter, _hits, query.Distance);
                    break;
            }

            int written = 0;
            int limit = Math.Min(Math.Min(hitCount, _hits.Length), results.Length);
            for (int i = 0; i < limit; i++)
            {
                RaycastHit2D hit = _hits[i];
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
                    position: new TrajectoryVector3(hit.point.x, hit.point.y, query.From.Z),
                    normal: new TrajectoryVector3(hit.normal.x, hit.normal.y, 0f),
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
    }
}
