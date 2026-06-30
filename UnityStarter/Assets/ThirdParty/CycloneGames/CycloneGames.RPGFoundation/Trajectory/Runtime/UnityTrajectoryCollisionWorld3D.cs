using System;
using CycloneGames.RPGFoundation.Trajectory.Core;
using UnityEngine;

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
            TrajectoryHit[] results,
            int maxResults)
        {
            if (results == null || maxResults <= 0 || query.Distance <= EPSILON)
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
            int hitCount = query.Radius > EPSILON
                ? Physics.SphereCastNonAlloc(origin, query.Radius, direction, _hits, query.Distance, query.CollisionLayerMask, _queryTriggerInteraction)
                : Physics.RaycastNonAlloc(origin, direction, _hits, query.Distance, query.CollisionLayerMask, _queryTriggerInteraction);

            int written = 0;
            int limit = Math.Min(Math.Min(hitCount, _hits.Length), Math.Min(maxResults, results.Length));
            for (int i = 0; i < limit; i++)
            {
                RaycastHit hit = _hits[i];
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
