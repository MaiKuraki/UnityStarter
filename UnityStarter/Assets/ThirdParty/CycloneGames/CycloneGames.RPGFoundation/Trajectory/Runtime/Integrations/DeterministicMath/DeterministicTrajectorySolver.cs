using System;
using CycloneGames.DeterministicMath;
using CycloneGames.RPGFoundation.Trajectory.Core;

namespace CycloneGames.RPGFoundation.Trajectory.Integrations.DeterministicMath
{
    /// <summary>
    /// Fixed-point counterpart of <see cref="TrajectorySolver"/>. Behaviour is kept in lockstep with
    /// the float Core: nearest-hit selection with a deterministic tie-break and pierce repeat
    /// suppression, atomic hit/segment commit, scratch clearing, an untrusted-position fallback,
    /// a bounded iteration budget and NearestHitIndex all mirror the float implementation. Only the
    /// arithmetic type differs.
    /// </summary>
    public static class DeterministicTrajectorySolver
    {
        /// <summary>Recent pierce hits inspected by the repeat filter. Mirrors the float Core.</summary>
        private const int PIERCE_HISTORY_WINDOW = 8;

        public static DeterministicTrajectoryTraceResult Trace(
            in DeterministicTrajectoryQuery query,
            IDeterministicTrajectoryCollisionWorld collisionWorld,
            DeterministicTrajectoryTraceBuffer buffer)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException(nameof(buffer));
            }

            buffer.Clear();
            FPVector3 endPosition = query.Origin;

            if (!query.IsValid)
            {
                return new DeterministicTrajectoryTraceResult(
                    TrajectoryTraceFlags.InvalidQuery,
                    buffer.SegmentCount,
                    buffer.HitCount,
                    FPInt64.Zero,
                    endPosition);
            }

            FPVector3 direction = NormalizeOrZero(query.Direction);
            if (direction.SqrMagnitude.RawValue == 0)
            {
                return new DeterministicTrajectoryTraceResult(
                    TrajectoryTraceFlags.DegenerateDirection,
                    buffer.SegmentCount,
                    buffer.HitCount,
                    FPInt64.Zero,
                    endPosition);
            }

            if (collisionWorld == null)
            {
                endPosition = query.Origin + direction * query.MaxDistance;
                TrajectoryTraceFlags flags = TryAddSegment(
                    buffer, query.Origin, endPosition, direction, query.MaxDistance, -1, out _);

                return new DeterministicTrajectoryTraceResult(
                    flags | TrajectoryTraceFlags.MissingCollisionWorld,
                    buffer.SegmentCount,
                    buffer.HitCount,
                    query.MaxDistance,
                    endPosition);
            }

            return TraceWithCollisionWorld(in query, collisionWorld, buffer, direction);
        }

        private static DeterministicTrajectoryTraceResult TraceWithCollisionWorld(
            in DeterministicTrajectoryQuery query,
            IDeterministicTrajectoryCollisionWorld collisionWorld,
            DeterministicTrajectoryTraceBuffer buffer,
            FPVector3 direction)
        {
            TrajectoryTraceFlags flags = TrajectoryTraceFlags.None;
            FPVector3 from = query.Origin;
            FPVector3 endPosition = query.Origin;
            FPInt64 remainingDistance = query.MaxDistance;
            FPInt64 travelDistance = FPInt64.Zero;
            int remainingReflections = query.MaxReflectionCount;
            int remainingPierces = query.MaxPierceCount;
            ulong ignoredTargetEntityId = query.InitialIgnoredTargetEntityId;
            ulong ignoredTargetObjectId = query.InitialIgnoredTargetObjectId;
            int nearestHitIndex = -1;
            bool hasNearestHit = false;
            FPInt64 nearestPathDistance = FPInt64.Zero;

            for (int iteration = 0; iteration < query.MaxIterationCount; iteration++)
            {
                if (remainingDistance.RawValue <= 0)
                {
                    return Build(flags, buffer, travelDistance, endPosition, nearestHitIndex);
                }

                FPVector3 to = from + direction * remainingDistance;
                var castQuery = new DeterministicTrajectoryCastQuery(
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

                int hitCount = collisionWorld.Cast(in castQuery, buffer.CastHits);

                // Remember what the adapter claims it wrote so the next Clear wipes exactly that
                // range. A count above capacity is clamped; the read loop clamps as well.
                buffer.MarkCastHitsWritten(hitCount);

                if (hitCount <= 0 || !TrySelectNearestHit(
                        buffer.CastHits,
                        hitCount,
                        buffer.Hits,
                        buffer.HitCount,
                        out DeterministicTrajectoryHit hit))
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

                    return Build(flags, buffer, travelDistance, endPosition, nearestHitIndex);
                }

                FPInt64 hitDistance = ClampDistance(hit.Distance, remainingDistance);
                FPVector3 hitPosition = ResolveHitPosition(from, direction, hit, hitDistance);

                // Commit the hit and its segment together. Checking both capacities before writing
                // keeps HitCount <= SegmentCount and makes every hit's SegmentIndex resolvable.
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
                TrajectoryTraceFlags addSegmentFlags = TryAddSegment(
                    buffer, from, hitPosition, direction, hitDistance, hitIndex, out int segmentIndex);
                flags |= addSegmentFlags;
                if ((addSegmentFlags & TrajectoryTraceFlags.SegmentCapacityReached) != 0)
                {
                    return Build(flags, buffer, travelDistance, endPosition, nearestHitIndex);
                }

                var storedHit = new DeterministicTrajectoryHit(
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

                if (!hasNearestHit || travelDistance < nearestPathDistance)
                {
                    nearestPathDistance = travelDistance;
                    nearestHitIndex = hitIndex;
                    hasNearestHit = true;
                }

                if (buffer.HitCount >= query.MaxHitCount)
                {
                    flags |= TrajectoryTraceFlags.MaxHitCountReached;
                    return Build(flags, buffer, travelDistance, endPosition, nearestHitIndex);
                }

                if (storedHit.Response == TrajectoryHitResponse.Reflect && remainingReflections > 0)
                {
                    FPVector3 normal = NormalizeOrZero(storedHit.Normal);
                    FPVector3 reflected = NormalizeOrZero(Reflect(direction, normal));
                    if (normal.SqrMagnitude.RawValue == 0 || reflected.SqrMagnitude.RawValue == 0)
                    {
                        return Build(flags, buffer, travelDistance, endPosition, nearestHitIndex);
                    }

                    remainingReflections--;
                    remainingDistance -= hitDistance;
                    direction = reflected;
                    from = hitPosition + normal * query.SurfaceOffset;
                    ignoredTargetEntityId = storedHit.TargetEntityId;
                    ignoredTargetObjectId = storedHit.TargetObjectId;
                    continue;
                }

                if (storedHit.Response == TrajectoryHitResponse.Pierce && remainingPierces > 0)
                {
                    remainingPierces--;
                    remainingDistance -= hitDistance + query.SurfaceOffset;
                    from = hitPosition + direction * query.SurfaceOffset;
                    ignoredTargetEntityId = storedHit.TargetEntityId;
                    ignoredTargetObjectId = storedHit.TargetObjectId;
                    continue;
                }

                return Build(flags, buffer, travelDistance, endPosition, nearestHitIndex);
            }

            flags |= TrajectoryTraceFlags.IterationLimitReached;
            return Build(flags, buffer, travelDistance, endPosition, nearestHitIndex);
        }

        private static DeterministicTrajectoryTraceResult Build(
            TrajectoryTraceFlags flags,
            DeterministicTrajectoryTraceBuffer buffer,
            FPInt64 travelDistance,
            FPVector3 endPosition,
            int nearestHitIndex)
        {
            return new DeterministicTrajectoryTraceResult(
                flags,
                buffer.SegmentCount,
                buffer.HitCount,
                travelDistance,
                endPosition,
                nearestHitIndex);
        }

        private static TrajectoryTraceFlags TryAddSegment(
            DeterministicTrajectoryTraceBuffer buffer,
            FPVector3 from,
            FPVector3 to,
            FPVector3 direction,
            FPInt64 distance,
            int hitIndex,
            out int segmentIndex)
        {
            segmentIndex = buffer.SegmentCount;
            var segment = new DeterministicTrajectorySegment(
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
            DeterministicTrajectoryHit[] hits,
            int hitCount,
            DeterministicTrajectoryHit[] recordedHits,
            int recordedHitCount,
            out DeterministicTrajectoryHit selected)
        {
            selected = default;
            bool hasSelected = false;
            FPInt64 bestDistance = FPInt64.Zero;
            int limit = Math.Min(hitCount, hits.Length);

            // Resolve the pierce history once instead of per candidate: the common Stop/Reflect
            // traces have no pierce history and pay nothing for the filter.
            bool filterPiercedTargets = HasPierceHistory(recordedHits, recordedHitCount);

            for (int i = 0; i < limit; i++)
            {
                DeterministicTrajectoryHit hit = hits[i];
                if (!hit.IsValid || !IsKnownResponse(hit.Response))
                {
                    continue;
                }

                // A pierce continuation must not re-hit a target that a previous pierce already
                // passed through. Reflection deliberately skips this filter.
                if (filterPiercedTargets && IsPiercedTargetRepeat(hit, recordedHits, recordedHitCount))
                {
                    continue;
                }

                if (!hasSelected || hit.Distance < bestDistance
                    || (hit.Distance == bestDistance && IsDeterministicTieBreak(hit, selected)))
                {
                    selected = hit;
                    bestDistance = hit.Distance;
                    hasSelected = true;
                }
            }

            return hasSelected;
        }

        private static bool HasPierceHistory(
            DeterministicTrajectoryHit[] recordedHits,
            int recordedHitCount)
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

        private static bool IsPiercedTargetRepeat(
            in DeterministicTrajectoryHit candidate,
            DeterministicTrajectoryHit[] recordedHits,
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
                DeterministicTrajectoryHit previous = recordedHits[i];
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
            in DeterministicTrajectoryHit candidate,
            in DeterministicTrajectoryHit current)
        {
            if (candidate.TargetEntityId != current.TargetEntityId)
            {
                return candidate.TargetEntityId != 0UL
                    && (current.TargetEntityId == 0UL || candidate.TargetEntityId < current.TargetEntityId);
            }

            return candidate.TargetObjectId != 0
                && (current.TargetObjectId == 0 || candidate.TargetObjectId < current.TargetObjectId);
        }

        private static bool IsKnownResponse(TrajectoryHitResponse response)
        {
            return (uint)response <= (uint)TrajectoryHitResponse.Pierce;
        }

        /// <summary>
        /// Counterpart of TrajectorySolver.ResolveHitPosition. The reported position is untrusted:
        /// accept it only when it agrees with From + Direction * Distance. Comparisons use raw
        /// Q32.32 units in linear space, so no squaring can overflow.
        /// </summary>
        private static FPVector3 ResolveHitPosition(
            FPVector3 from,
            FPVector3 direction,
            in DeterministicTrajectoryHit hit,
            FPInt64 hitDistance)
        {
            FPVector3 expected = from + direction * hitDistance;

            long tolerance = hitDistance.RawValue / 1000L;
            long minimum = FPInt64.FromFloat(0.0001f).RawValue;
            if (tolerance < minimum)
            {
                tolerance = minimum;
            }

            FPVector3 error = hit.Position - expected;
            if (AbsRaw(error.X.RawValue) <= tolerance
                && AbsRaw(error.Y.RawValue) <= tolerance
                && AbsRaw(error.Z.RawValue) <= tolerance)
            {
                return hit.Position;
            }

            return expected;
        }

        private static long AbsRaw(long value)
        {
            if (value == long.MinValue)
            {
                return long.MaxValue;
            }

            return value < 0L ? -value : value;
        }

        private static FPInt64 ClampDistance(FPInt64 distance, FPInt64 maxDistance)
        {
            if (distance.RawValue <= 0)
            {
                return FPInt64.Zero;
            }

            return distance < maxDistance ? distance : maxDistance;
        }

        private static FPVector3 NormalizeOrZero(FPVector3 value)
        {
            return value.NormalizedOrZero;
        }

        private static FPVector3 Reflect(FPVector3 vector, FPVector3 normal)
        {
            return vector - normal * (FPInt64.FromInt(2) * FPVector3.Dot(vector, normal));
        }
    }
}
