using System;

namespace CycloneGames.RPGFoundation.Trajectory.Core
{
    public static class TrajectorySolver
    {
        private const float EPSILON = 0.000001f;

        /// <summary>
        /// How many recent pierce hits the repeat filter inspects. Beyond this the ray has already
        /// travelled past those targets, so scanning the rest adds O(n²) cost for entries that
        /// cannot repeat.
        /// </summary>
        private const int PIERCE_HISTORY_WINDOW = 8;

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
            ulong ignoredTargetObjectId = query.InitialIgnoredTargetObjectId;
            int nearestHitIndex = -1;
            float nearestPathDistance = float.PositiveInfinity;

            for (int iteration = 0; iteration < query.MaxIterationCount; iteration++)
            {
                if (remainingDistance <= EPSILON)
                {
                    return new TrajectoryTraceResult(
                        flags,
                        buffer.SegmentCount,
                        buffer.HitCount,
                        travelDistance,
                        endPosition, nearestHitIndex);
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
                    ignoredTargetObjectId,
                    query.Shape,
                    query.Height,
                    query.HalfExtents);

                int hitCount = collisionWorld.Cast(
                    in castQuery,
                    buffer.CastHits);

                // Remember what the adapter claims it wrote so the next Clear wipes exactly that
                // range. A count above capacity is clamped; the read loop clamps as well.
                buffer.MarkCastHitsWritten(hitCount);

                if (hitCount <= 0 || !TrySelectNearestHit(
                        buffer.CastHits,
                        hitCount,
                        buffer.Hits,
                        buffer.HitCount,
                        out TrajectoryHit hit))
                {
                    TrajectoryTraceFlags segmentFlags = TryAddSegment(
                        buffer, from, to, direction, remainingDistance, -1, out _);
                    flags |= segmentFlags;

                    // TravelDistance accounts for recorded segments only. When the final segment
                    // cannot be stored, claiming the remaining distance would make the result
                    // disagree with the buffer.
                    if ((segmentFlags & TrajectoryTraceFlags.SegmentCapacityReached) == 0)
                    {
                        travelDistance += remainingDistance;
                        endPosition = to;
                    }

                    return new TrajectoryTraceResult(
                        flags,
                        buffer.SegmentCount,
                        buffer.HitCount,
                        travelDistance,
                        endPosition, nearestHitIndex);
                }

                float hitDistance = ClampDistance(hit.Distance, remainingDistance);
                TrajectoryVector3 hitPosition = ResolveHitPosition(from, direction, hit, hitDistance);

                // Commit the hit and its segment together. Checking both capacities before writing
                // keeps HitCount <= SegmentCount and makes every Hit.SegmentIndex resolvable.
                if (buffer.HitCount >= buffer.HitCapacity)
                {
                    flags |= TrajectoryTraceFlags.HitCapacityReached;
                    return new TrajectoryTraceResult(
                        flags,
                        buffer.SegmentCount,
                        buffer.HitCount,
                        travelDistance,
                        endPosition, nearestHitIndex);
                }

                if (buffer.SegmentCount >= buffer.SegmentCapacity)
                {
                    flags |= TrajectoryTraceFlags.SegmentCapacityReached;
                    return new TrajectoryTraceResult(
                        flags,
                        buffer.SegmentCount,
                        buffer.HitCount,
                        travelDistance,
                        endPosition, nearestHitIndex);
                }

                int hitIndex = buffer.HitCount;
                flags |= TryAddSegment(buffer, from, hitPosition, direction, hitDistance, hitIndex, out int segmentIndex);
                if ((flags & TrajectoryTraceFlags.SegmentCapacityReached) != 0)
                {
                    return new TrajectoryTraceResult(
                        flags,
                        buffer.SegmentCount,
                        buffer.HitCount,
                        travelDistance,
                        endPosition, nearestHitIndex);
                }

                TrajectoryHit storedHit = new TrajectoryHit(
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
                    return new TrajectoryTraceResult(
                        flags,
                        buffer.SegmentCount,
                        buffer.HitCount,
                        travelDistance,
                        endPosition, nearestHitIndex);
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
                    return new TrajectoryTraceResult(
                        flags,
                        buffer.SegmentCount,
                        buffer.HitCount,
                        travelDistance,
                        endPosition, nearestHitIndex);
                }

                TrajectoryHitResponse response = storedHit.Response;
                if (response == TrajectoryHitResponse.Reflect && remainingReflections > 0)
                {
                    TrajectoryVector3 normal = storedHit.Normal.NormalizedOrZero();
                    TrajectoryVector3 reflected = TrajectoryVector3.Reflect(direction, normal).NormalizedOrZero();
                    if (normal.LengthSquared <= EPSILON || reflected.LengthSquared <= EPSILON)
                    {
                        return new TrajectoryTraceResult(flags, buffer.SegmentCount, buffer.HitCount, travelDistance, endPosition, nearestHitIndex);
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
                    endPosition, nearestHitIndex);
            }

            flags |= TrajectoryTraceFlags.IterationLimitReached;
            return new TrajectoryTraceResult(
                flags,
                buffer.SegmentCount,
                buffer.HitCount,
                travelDistance,
                endPosition, nearestHitIndex);
        }

        internal static TrajectoryTraceFlags TryAddSegment(
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

        internal static bool TrySelectNearestHit(
            TrajectoryHit[] hits,
            int hitCount,
            TrajectoryHit[] recordedHits,
            int recordedHitCount,
            out TrajectoryHit selected)
        {
            selected = default;
            float bestDistance = float.PositiveInfinity;
            int limit = Math.Min(hitCount, hits.Length);

            // Resolve the pierce history once instead of per candidate: the common Stop/Reflect
            // traces have no pierce history and pay nothing for the filter.
            bool filterPiercedTargets = HasPierceHistory(recordedHits, recordedHitCount);

            for (int i = 0; i < limit; i++)
            {
                TrajectoryHit hit = hits[i];
                if (!hit.IsValid || !IsKnownResponse(hit.Response))
                {
                    continue;
                }

                // A pierce continuation must not re-hit a target that a previous pierce already
                // passed through. Reflection deliberately skips this filter: bouncing back onto
                // the same surface is legitimate. The adapter may also filter, which is redundant
                // but harmless, so correctness no longer depends on adapter cooperation.
                if (filterPiercedTargets && IsPiercedTargetRepeat(hit, recordedHits, recordedHitCount))
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

        /// <summary>
        /// Nearest-hit selection without the pierce repeat filter. A fixed polyline path may cross the
        /// same target more than once on purpose, so repeats must be reported rather than suppressed.
        /// </summary>
        internal static bool TrySelectNearestHit(
            TrajectoryHit[] hits,
            int hitCount,
            out TrajectoryHit selected)
        {
            return TrySelectNearestHit(hits, hitCount, Array.Empty<TrajectoryHit>(), 0, out selected);
        }

        private static bool HasPierceHistory(TrajectoryHit[] recordedHits, int recordedHitCount)
        {
            int limit = Math.Min(recordedHitCount, recordedHits.Length);
            int first = limit - PIERCE_HISTORY_WINDOW;
            if (first < 0)
            {
                first = 0;
            }

            for (int i = first; i < limit; i++)
            {
                if (recordedHits[i].Response == TrajectoryHitResponse.Pierce)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsKnownResponse(TrajectoryHitResponse response)
        {
            return (uint)response <= (uint)TrajectoryHitResponse.Pierce;
        }

        private static bool IsPiercedTargetRepeat(
            in TrajectoryHit candidate,
            TrajectoryHit[] recordedHits,
            int recordedHitCount)
        {
            if (candidate.TargetEntityId == 0UL && candidate.TargetObjectId == 0UL)
            {
                return false;
            }

            int limit = Math.Min(recordedHitCount, recordedHits.Length);
            int first = limit - PIERCE_HISTORY_WINDOW;
            if (first < 0)
            {
                first = 0;
            }

            for (int i = first; i < limit; i++)
            {
                TrajectoryHit previous = recordedHits[i];
                if (previous.Response != TrajectoryHitResponse.Pierce)
                {
                    continue;
                }

                if (candidate.TargetEntityId != 0UL && previous.TargetEntityId == candidate.TargetEntityId)
                {
                    return true;
                }

                if (candidate.TargetObjectId != 0UL && previous.TargetObjectId == candidate.TargetObjectId)
                {
                    return true;
                }
            }

            return false;
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

        internal static float ClampDistance(float distance, float maxDistance)
        {
            if (distance <= 0f)
            {
                return 0f;
            }

            return distance < maxDistance ? distance : maxDistance;
        }

        internal static TrajectoryVector3 ResolveHitPosition(
            TrajectoryVector3 from,
            TrajectoryVector3 direction,
            in TrajectoryHit hit,
            float hitDistance)
        {
            if (!hit.Position.IsFinite)
            {
                return from + direction * hitDistance;
            }

            // The reported position is an untrusted input: an adapter may hand back a point that
            // is not on the swept segment, or one whose offset disagrees with the reported distance.
            // Accept it only when it lies on the segment and its length matches the distance.
            TrajectoryVector3 delta = hit.Position - from;
            float tolerance = hitDistance * 0.001f;
            if (tolerance < 0.0001f)
            {
                tolerance = 0.0001f;
            }

            // Squared comparison avoids a sqrt on the hot path. tolerance² is far below
            // float resolution at typical distances, so compare against the linear bound instead.
            float along = TrajectoryVector3.Dot(delta, direction);
            float lengthSquared = delta.LengthSquared;
            float upperDistance = hitDistance + tolerance;
            float lowerDistance = hitDistance - tolerance;
            if (lowerDistance < 0f)
            {
                lowerDistance = 0f;
            }

            bool insideSegment = along >= -tolerance && along <= upperDistance;
            bool lengthAgrees = lengthSquared >= lowerDistance * lowerDistance
                                && lengthSquared <= upperDistance * upperDistance;

            return insideSegment && lengthAgrees
                ? hit.Position
                : from + direction * hitDistance;
        }
    }
}
