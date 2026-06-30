using System;
using CycloneGames.RPGFoundation.Trajectory.Core;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Trajectory.Runtime
{
    public sealed class UnityTrajectoryCollisionWorld2D : ITrajectoryCollisionWorld
    {
        private const float EPSILON = 0.000001f;

        private readonly RaycastHit2D[] _hits;
        private readonly int _reflectionLayerMask;
        private readonly int _pierceLayerMask;
        private readonly float _minDepth;
        private readonly float _maxDepth;

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
            _minDepth = minDepth;
            _maxDepth = maxDepth;
        }

        public int Cast(
            in TrajectoryCastQuery query,
            TrajectoryHit[] results,
            int maxResults)
        {
            if (results == null || maxResults <= 0 || query.Distance <= EPSILON)
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
            int hitCount = query.Radius > EPSILON
                ? Physics2D.CircleCastNonAlloc(origin, query.Radius, direction, _hits, query.Distance, query.CollisionLayerMask, _minDepth, _maxDepth)
                : Physics2D.RaycastNonAlloc(origin, direction, _hits, query.Distance, query.CollisionLayerMask, _minDepth, _maxDepth);

            int written = 0;
            int limit = Math.Min(Math.Min(hitCount, _hits.Length), Math.Min(maxResults, results.Length));
            for (int i = 0; i < limit; i++)
            {
                RaycastHit2D hit = _hits[i];
                if (hit.collider == null)
                {
                    continue;
                }

                int objectId = hit.collider.GetInstanceID();
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
