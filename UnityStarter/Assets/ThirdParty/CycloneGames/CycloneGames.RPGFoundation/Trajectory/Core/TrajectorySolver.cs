using System;

namespace CycloneGames.RPGFoundation.Trajectory.Core
{
    public static class TrajectorySolver
    {
        private const float EPSILON = 0.000001f;

        public static TrajectoryTraceResult Trace(
            in TrajectoryQuery query,
            ITrajectoryCollisionWorld collisionWorld,
            TrajectoryTraceBuffer buffer)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            buffer.Clear();
            TrajectoryVector3 endPosition = query.Origin;

            if (!query.IsValid)
            {
                return new TrajectoryTraceResult(
                    TrajectoryTraceFlags.InvalidQuery,
                    buffer.SegmentCount,
                    buffer.HitCount,
                    0f,
                    endPosition);
            }

            TrajectoryVector3 direction = query.Direction.NormalizedOrZero();
            if (direction.LengthSquared <= EPSILON)
            {
                return new TrajectoryTraceResult(
                    TrajectoryTraceFlags.DegenerateDirection,
                    buffer.SegmentCount,
                    buffer.HitCount,
                    0f,
                    endPosition);
            }

            if (collisionWorld == null)
            {
                endPosition = query.Origin + direction * query.MaxDistance;
                TrajectoryTraceFlags flags = TryAddSegment(
                    buffer,
                    query.Origin,
                    endPosition,
                    direction,
                    query.MaxDistance,
                    -1,
                    out _);

                return new TrajectoryTraceResult(
                    flags | TrajectoryTraceFlags.MissingCollisionWorld,
                    buffer.SegmentCount,
                    buffer.HitCount,
                    query.MaxDistance,
                    endPosition);
            }

            return TraceWithCollisionWorld(in query, collisionWorld, buffer, direction);
        }

        private static TrajectoryTraceResult TraceWithCollisionWorld(
            in TrajectoryQuery query,
            ITrajectoryCollisionWorld collisionWorld,
            TrajectoryTraceBuffer buffer,
            TrajectoryVector3 direction)
        {
            TrajectoryTraceFlags flags = TrajectoryTraceFlags.None;
            TrajectoryVector3 from = query.Origin;
            TrajectoryVector3 endPosition = query.Origin;
            float remainingDistance = query.MaxDistance;
            float travelDistance = 0f;
            int remainingReflections = query.MaxReflectionCount;
            int remainingPierces = query.MaxPierceCount;
            ulong ignoredTargetEntityId = query.InitialIgnoredTargetEntityId;
            int ignoredTargetObjectId = query.InitialIgnoredTargetObjectId;

            for (int iteration = 0; iteration < query.MaxIterationCount; iteration++)
            {
                if (remainingDistance <= EPSILON)
                {
                    return new TrajectoryTraceResult(
                        flags,
                        buffer.SegmentCount,
                        buffer.HitCount,
                        travelDistance,
                        endPosition);
                }

                TrajectoryVector3 to = from + direction * remainingDistance;
                var castQuery = new TrajectoryCastQuery(
                    query.TraceId,
                    buffer.SegmentCount,
                    query.OwnerEntityId,
                    query.CollisionLayerMask,
                    query.Radius,
                    remainingDistance,
                    from,
                    to,
                    direction,
                    ignoredTargetEntityId,
                    ignoredTargetObjectId);

                int hitCount = collisionWorld.Cast(
                    in castQuery,
                    buffer.CastHits,
                    buffer.CastHitCapacity);

                if (hitCount <= 0 || !TrySelectNearestHit(buffer.CastHits, hitCount, out TrajectoryHit hit))
                {
                    flags |= TryAddSegment(buffer, from, to, direction, remainingDistance, -1, out _);
                    travelDistance += remainingDistance;
                    endPosition = to;
                    return new TrajectoryTraceResult(
                        flags,
                        buffer.SegmentCount,
                        buffer.HitCount,
                        travelDistance,
                        endPosition);
                }

                float hitDistance = ClampDistance(hit.Distance, remainingDistance);
                TrajectoryVector3 hitPosition = ResolveHitPosition(from, direction, hit, hitDistance);
                int hitIndex = buffer.HitCount;
                TrajectoryHit storedHit = new TrajectoryHit(
                    hit.TargetEntityId,
                    hit.TargetObjectId,
                    hit.HitLayerMask,
                    hitDistance,
                    hitPosition,
                    hit.Normal,
                    hit.Response,
                    buffer.SegmentCount);

                if (!buffer.TryAddHit(in storedHit))
                {
                    flags |= TrajectoryTraceFlags.HitCapacityReached;
                    return new TrajectoryTraceResult(
                        flags,
                        buffer.SegmentCount,
                        buffer.HitCount,
                        travelDistance,
                        endPosition);
                }

                flags |= TryAddSegment(buffer, from, hitPosition, direction, hitDistance, hitIndex, out _);
                if ((flags & TrajectoryTraceFlags.SegmentCapacityReached) != 0)
                {
                    return new TrajectoryTraceResult(
                        flags,
                        buffer.SegmentCount,
                        buffer.HitCount,
                        travelDistance,
                        endPosition);
                }

                travelDistance += hitDistance;
                endPosition = hitPosition;

                if (buffer.HitCount >= query.MaxHitCount)
                {
                    flags |= TrajectoryTraceFlags.MaxHitCountReached;
                    return new TrajectoryTraceResult(
                        flags,
                        buffer.SegmentCount,
                        buffer.HitCount,
                        travelDistance,
                        endPosition);
                }

                TrajectoryHitResponse response = storedHit.Response;
                if (response == TrajectoryHitResponse.Reflect && remainingReflections > 0)
                {
                    TrajectoryVector3 normal = storedHit.Normal.NormalizedOrZero();
                    TrajectoryVector3 reflected = TrajectoryVector3.Reflect(direction, normal).NormalizedOrZero();
                    if (normal.LengthSquared <= EPSILON || reflected.LengthSquared <= EPSILON)
                    {
                        return new TrajectoryTraceResult(flags, buffer.SegmentCount, buffer.HitCount, travelDistance, endPosition);
                    }

                    remainingReflections--;
                    remainingDistance -= hitDistance;
                    direction = reflected;
                    from = hitPosition + normal * query.SurfaceOffset;
                    ignoredTargetEntityId = storedHit.TargetEntityId;
                    ignoredTargetObjectId = storedHit.TargetObjectId;
                    continue;
                }

                if (response == TrajectoryHitResponse.Pierce && remainingPierces > 0)
                {
                    remainingPierces--;
                    remainingDistance -= hitDistance + query.SurfaceOffset;
                    from = hitPosition + direction * query.SurfaceOffset;
                    ignoredTargetEntityId = storedHit.TargetEntityId;
                    ignoredTargetObjectId = storedHit.TargetObjectId;
                    continue;
                }

                return new TrajectoryTraceResult(
                    flags,
                    buffer.SegmentCount,
                    buffer.HitCount,
                    travelDistance,
                    endPosition);
            }

            flags |= TrajectoryTraceFlags.IterationLimitReached;
            return new TrajectoryTraceResult(
                flags,
                buffer.SegmentCount,
                buffer.HitCount,
                travelDistance,
                endPosition);
        }

        private static TrajectoryTraceFlags TryAddSegment(
            TrajectoryTraceBuffer buffer,
            TrajectoryVector3 from,
            TrajectoryVector3 to,
            TrajectoryVector3 direction,
            float distance,
            int hitIndex,
            out int segmentIndex)
        {
            segmentIndex = buffer.SegmentCount;
            var segment = new TrajectorySegment(
                segmentIndex,
                hitIndex,
                from,
                to,
                direction,
                distance);

            return buffer.TryAddSegment(in segment)
                ? TrajectoryTraceFlags.None
                : TrajectoryTraceFlags.SegmentCapacityReached;
        }

        private static bool TrySelectNearestHit(
            TrajectoryHit[] hits,
            int hitCount,
            out TrajectoryHit selected)
        {
            selected = default;
            float bestDistance = float.PositiveInfinity;
            int limit = Math.Min(hitCount, hits.Length);

            for (int i = 0; i < limit; i++)
            {
                TrajectoryHit hit = hits[i];
                if (!hit.IsValid)
                {
                    continue;
                }

                if (hit.Distance < bestDistance - EPSILON || IsDeterministicTieBreak(hit, selected, bestDistance))
                {
                    selected = hit;
                    bestDistance = hit.Distance;
                }
            }

            return selected.IsValid;
        }

        private static bool IsDeterministicTieBreak(
            in TrajectoryHit candidate,
            in TrajectoryHit current,
            float bestDistance)
        {
            if (!current.IsValid || Math.Abs(candidate.Distance - bestDistance) > EPSILON)
            {
                return false;
            }

            if (candidate.TargetEntityId != current.TargetEntityId)
            {
                return candidate.TargetEntityId != 0UL
                    && (current.TargetEntityId == 0UL || candidate.TargetEntityId < current.TargetEntityId);
            }

            return candidate.TargetObjectId != 0
                && (current.TargetObjectId == 0 || candidate.TargetObjectId < current.TargetObjectId);
        }

        private static float ClampDistance(float distance, float maxDistance)
        {
            if (distance <= 0f)
            {
                return 0f;
            }

            return distance < maxDistance ? distance : maxDistance;
        }

        private static TrajectoryVector3 ResolveHitPosition(
            TrajectoryVector3 from,
            TrajectoryVector3 direction,
            in TrajectoryHit hit,
            float hitDistance)
        {
            return hit.Position.IsFinite
                ? hit.Position
                : from + direction * hitDistance;
        }
    }
}
