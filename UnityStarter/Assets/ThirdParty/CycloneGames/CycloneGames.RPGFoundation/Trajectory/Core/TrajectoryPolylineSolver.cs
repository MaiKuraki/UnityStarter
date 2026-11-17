using System;

namespace CycloneGames.RPGFoundation.Trajectory.Core
{
    /// <summary>
    /// Solves collision along a caller-supplied polyline.
    ///
    /// This is the entry point for paths whose shape comes from somewhere else: Bezier or sine
    /// curves sampled upstream, patrol waypoints, portal paths, or any authored point list. The
    /// solver owns collision only — path shape stays with the caller, which keeps Core free of
    /// curve math while covering every curve type through one API.
    ///
    /// The path is fixed: a hit is recorded and the trace continues along the same polyline.
    /// <c>Reflect</c> and <c>Pierce</c> do not bend the path, and the reflection / pierce budgets are
    /// ignored. Only <c>Stop</c> ends the trace early. A caller that wants reflection after a hit
    /// should build the reflected path itself and trace it again.
    /// </summary>
    public static class TrajectoryPolylineSolver
    {
        private const float EPSILON = 0.000001f;

        /// <summary>
        /// Sweeps every non-degenerate segment of <paramref name="path"/> and records segments and
        /// hits into <paramref name="buffer"/>.
        ///
        /// <paramref name="query"/> supplies the sweep shape, layer mask, hit budget and distance
        /// budget. Its origin and direction are unused — the path defines both. Because of that this
        /// method validates the query without the direction check that
        /// <see cref="TrajectoryQuery.IsValid"/> applies.
        /// </summary>
        public static TrajectoryTraceResult Trace(
            ReadOnlySpan<TrajectoryVector3> path,
            in TrajectoryQuery query,
            ITrajectoryCollisionWorld collisionWorld,
            TrajectoryTraceBuffer buffer)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            buffer.Clear();
            TrajectoryVector3 start = path.Length > 0 ? path[0] : query.Origin;

            if (!IsQueryValidForPolyline(in query) || path.Length < 2)
            {
                return new TrajectoryTraceResult(
                    TrajectoryTraceFlags.InvalidQuery,
                    buffer.SegmentCount,
                    buffer.HitCount,
                    0f,
                    start);
            }

            for (int i = 0; i < path.Length; i++)
            {
                if (!path[i].IsFinite)
                {
                    return new TrajectoryTraceResult(
                        TrajectoryTraceFlags.InvalidQuery,
                        buffer.SegmentCount,
                        buffer.HitCount,
                        0f,
                        start);
                }
            }

            if (collisionWorld == null)
            {
                return TraceWithoutCollisionWorld(path, in query, buffer);
            }

            TrajectoryTraceFlags flags = TrajectoryTraceFlags.None;
            TrajectoryVector3 endPosition = start;
            float travelDistance = 0f;
            int nearestHitIndex = -1;
            float nearestPathDistance = float.PositiveInfinity;

            for (int pointIndex = 0; pointIndex + 1 < path.Length; pointIndex++)
            {
                TrajectoryVector3 segmentFrom = path[pointIndex];
                TrajectoryVector3 segmentTo = path[pointIndex + 1];

                // A single path segment may be split by several hits. MaxIterationCount bounds that
                // inner loop so a zero-distance adapter cannot spin forever.
                int slices = 0;
                while (true)
                {
                    if (slices++ >= query.MaxIterationCount)
                    {
                        flags |= TrajectoryTraceFlags.IterationLimitReached;
                        return Build(flags, buffer, travelDistance, endPosition, nearestHitIndex);
                    }

                    TrajectoryVector3 delta = segmentTo - segmentFrom;
                    float segmentLength = (float)Math.Sqrt(delta.LengthSquared);
                    if (segmentLength <= EPSILON)
                    {
                        break;
                    }

                    TrajectoryVector3 direction = delta / segmentLength;

                    if (travelDistance + segmentLength > query.MaxDistance)
                    {
                        float allowed = query.MaxDistance - travelDistance;
                        flags |= TrajectoryTraceFlags.PolylineTruncatedByMaxDistance;
                        if (allowed <= EPSILON)
                        {
                            return Build(flags, buffer, travelDistance, endPosition, nearestHitIndex);
                        }

                        segmentTo = segmentFrom + direction * allowed;
                        segmentLength = allowed;
                    }

                    var castQuery = new TrajectoryCastQuery(
                        query.TraceId,
                        buffer.SegmentCount,
                        query.OwnerEntityId,
                        query.CollisionLayerMask,
                        query.Radius,
                        segmentLength,
                        segmentFrom,
                        segmentTo,
                        direction,
                        0UL,
                        0UL,
                        query.Shape,
                        query.Height,
                        query.HalfExtents);

                    int hitCount = collisionWorld.Cast(in castQuery, buffer.CastHits);
                    buffer.MarkCastHitsWritten(hitCount);

                    if (hitCount <= 0 || !TrajectorySolver.TrySelectNearestHit(
                            buffer.CastHits,
                            hitCount,
                            out TrajectoryHit hit))
                    {
                        flags |= TrajectorySolver.TryAddSegment(
                            buffer,
                            segmentFrom,
                            segmentTo,
                            direction,
                            segmentLength,
                            -1,
                            out _);
                        travelDistance += segmentLength;
                        endPosition = segmentTo;
                        if ((flags & TrajectoryTraceFlags.SegmentCapacityReached) != 0)
                        {
                            return Build(flags, buffer, travelDistance, endPosition, nearestHitIndex);
                        }

                        break;
                    }

                    float hitDistance = TrajectorySolver.ClampDistance(hit.Distance, segmentLength);
                    TrajectoryVector3 hitPosition = TrajectorySolver.ResolveHitPosition(
                        segmentFrom, direction, hit, hitDistance);

                    if (buffer.HitCount >= buffer.HitCapacity)
                    {
                        flags |= TrajectoryTraceFlags.HitCapacityReached;
                        return Build(flags, buffer, travelDistance, endPosition, nearestHitIndex);
                    }

                    if (buffer.SegmentCount >= buffer.SegmentCapacity)
                    {
                        flags |= TrajectoryTraceFlags.SegmentCapacityReached;
                        return Build(flags, buffer, travelDistance, endPosition, nearestHitIndex);
                    }

                    int hitIndex = buffer.HitCount;
                    flags |= TrajectorySolver.TryAddSegment(
                        buffer,
                        segmentFrom,
                        hitPosition,
                        direction,
                        hitDistance,
                        hitIndex,
                        out int segmentIndex);
                    if ((flags & TrajectoryTraceFlags.SegmentCapacityReached) != 0)
                    {
                        return Build(flags, buffer, travelDistance, endPosition, nearestHitIndex);
                    }

                    var storedHit = new TrajectoryHit(
                        hit.TargetEntityId,
                        hit.TargetObjectId,
                        hit.HitLayerMask,
                        hitDistance,
                        hitPosition,
                        hit.Normal,
                        hit.Response,
                        segmentIndex);

                    if (!buffer.TryAddHit(in storedHit))
                    {
                        flags |= TrajectoryTraceFlags.HitCapacityReached;
                        return Build(flags, buffer, travelDistance, endPosition, nearestHitIndex);
                    }

                    travelDistance += hitDistance;
                    endPosition = hitPosition;

                    if (travelDistance < nearestPathDistance)
                    {
                        nearestPathDistance = travelDistance;
                        nearestHitIndex = hitIndex;
                    }

                    if (buffer.HitCount >= query.MaxHitCount)
                    {
                        flags |= TrajectoryTraceFlags.MaxHitCountReached;
                        return Build(flags, buffer, travelDistance, endPosition, nearestHitIndex);
                    }

                    if (storedHit.Response == TrajectoryHitResponse.Stop)
                    {
                        return Build(flags, buffer, travelDistance, endPosition, nearestHitIndex);
                    }

                    // Continue along the same polyline. Nudge the start past the surface so the swept
                    // shape does not immediately re-enter it when the hit had zero distance.
                    float nudge = query.SurfaceOffset > EPSILON ? query.SurfaceOffset : EPSILON;
                    segmentFrom = hitDistance > EPSILON
                        ? hitPosition
                        : hitPosition + direction * nudge;
                }
            }

            return Build(flags, buffer, travelDistance, endPosition, nearestHitIndex);
        }

        private static TrajectoryTraceResult TraceWithoutCollisionWorld(
            ReadOnlySpan<TrajectoryVector3> path,
            in TrajectoryQuery query,
            TrajectoryTraceBuffer buffer)
        {
            TrajectoryTraceFlags flags = TrajectoryTraceFlags.MissingCollisionWorld;
            TrajectoryVector3 endPosition = path[0];
            float travelDistance = 0f;

            for (int i = 0; i + 1 < path.Length; i++)
            {
                TrajectoryVector3 delta = path[i + 1] - path[i];
                float segmentLength = (float)Math.Sqrt(delta.LengthSquared);
                if (segmentLength <= EPSILON)
                {
                    continue;
                }

                TrajectoryVector3 direction = delta / segmentLength;
                if (travelDistance + segmentLength > query.MaxDistance)
                {
                    float allowed = query.MaxDistance - travelDistance;
                    flags |= TrajectoryTraceFlags.PolylineTruncatedByMaxDistance;
                    if (allowed <= EPSILON)
                    {
                        return Build(flags, buffer, travelDistance, endPosition, -1);
                    }

                    endPosition = path[i] + direction * allowed;
                    flags |= TrajectorySolver.TryAddSegment(
                        buffer, path[i], endPosition, direction, allowed, -1, out _);
                    return Build(flags, buffer, travelDistance + allowed, endPosition, -1);
                }

                flags |= TrajectorySolver.TryAddSegment(
                    buffer, path[i], path[i + 1], direction, segmentLength, -1, out _);
                travelDistance += segmentLength;
                endPosition = path[i + 1];

                if ((flags & TrajectoryTraceFlags.SegmentCapacityReached) != 0)
                {
                    return Build(flags, buffer, travelDistance, endPosition, -1);
                }
            }

            return Build(flags, buffer, travelDistance, endPosition, -1);
        }

        private static TrajectoryTraceResult Build(
            TrajectoryTraceFlags flags,
            TrajectoryTraceBuffer buffer,
            float travelDistance,
            TrajectoryVector3 endPosition,
            int nearestHitIndex)
        {
            return new TrajectoryTraceResult(
                flags,
                buffer.SegmentCount,
                buffer.HitCount,
                travelDistance,
                endPosition,
                nearestHitIndex);
        }

        private static bool IsQueryValidForPolyline(in TrajectoryQuery query)
        {
            return query.CollisionLayerMask != 0
                && query.MaxDistance > 0f
                && !float.IsNaN(query.MaxDistance)
                && !float.IsInfinity(query.MaxDistance)
                && query.MaxHitCount > 0
                && query.MaxIterationCount > 0
                && query.SurfaceOffset >= 0f
                && IsShapeValid(in query);
        }

        private static bool IsShapeValid(in TrajectoryQuery query)
        {
            switch (query.Shape)
            {
                case TrajectoryShape.Ray:
                    return true;
                case TrajectoryShape.Sphere:
                    return query.Radius > 0f;
                case TrajectoryShape.Capsule:
                    return query.Radius > 0f && query.Height >= query.Radius * 2f;
                case TrajectoryShape.Box:
                    return query.HalfExtents.X > 0f
                        && query.HalfExtents.Y > 0f
                        && query.HalfExtents.Z > 0f;
                default:
                    return false;
            }
        }
    }
}
