using System;
using CycloneGames.DeterministicMath;
using CycloneGames.RPGFoundation.Trajectory.Core;

namespace CycloneGames.RPGFoundation.Trajectory.Integrations.DeterministicMath
{
    public static class DeterministicTrajectorySolver
    {
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
                    buffer,
                    query.Origin,
                    endPosition,
                    direction,
                    query.MaxDistance,
                    -1);

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
            int ignoredTargetObjectId = query.InitialIgnoredTargetObjectId;

            for (int iteration = 0; iteration < query.MaxIterationCount; iteration++)
            {
                if (remainingDistance.RawValue <= 0)
                {
                    return new DeterministicTrajectoryTraceResult(
                        flags,
                        buffer.SegmentCount,
                        buffer.HitCount,
                        travelDistance,
                        endPosition);
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
                    ignoredTargetObjectId);

                int hitCount = collisionWorld.Cast(
                    in castQuery,
                    buffer.CastHits,
                    buffer.CastHitCapacity);

                if (hitCount <= 0 || !TrySelectNearestHit(buffer.CastHits, hitCount, out DeterministicTrajectoryHit hit))
                {
                    flags |= TryAddSegment(buffer, from, to, direction, remainingDistance, -1);
                    travelDistance += remainingDistance;
                    endPosition = to;
                    return new DeterministicTrajectoryTraceResult(flags, buffer.SegmentCount, buffer.HitCount, travelDistance, endPosition);
                }

                FPInt64 hitDistance = ClampDistance(hit.Distance, remainingDistance);
                FPVector3 hitPosition = hit.Position;
                int hitIndex = buffer.HitCount;
                var storedHit = new DeterministicTrajectoryHit(
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
                    return new DeterministicTrajectoryTraceResult(flags, buffer.SegmentCount, buffer.HitCount, travelDistance, endPosition);
                }

                flags |= TryAddSegment(buffer, from, hitPosition, direction, hitDistance, hitIndex);
                if ((flags & TrajectoryTraceFlags.SegmentCapacityReached) != 0)
                {
                    return new DeterministicTrajectoryTraceResult(flags, buffer.SegmentCount, buffer.HitCount, travelDistance, endPosition);
                }

                travelDistance += hitDistance;
                endPosition = hitPosition;

                if (buffer.HitCount >= query.MaxHitCount)
                {
                    flags |= TrajectoryTraceFlags.MaxHitCountReached;
                    return new DeterministicTrajectoryTraceResult(flags, buffer.SegmentCount, buffer.HitCount, travelDistance, endPosition);
                }

                if (storedHit.Response == TrajectoryHitResponse.Reflect && remainingReflections > 0)
                {
                    FPVector3 normal = NormalizeOrZero(storedHit.Normal);
                    FPVector3 reflected = NormalizeOrZero(Reflect(direction, normal));
                    if (normal.SqrMagnitude.RawValue == 0 || reflected.SqrMagnitude.RawValue == 0)
                    {
                        return new DeterministicTrajectoryTraceResult(flags, buffer.SegmentCount, buffer.HitCount, travelDistance, endPosition);
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

                return new DeterministicTrajectoryTraceResult(flags, buffer.SegmentCount, buffer.HitCount, travelDistance, endPosition);
            }

            flags |= TrajectoryTraceFlags.IterationLimitReached;
            return new DeterministicTrajectoryTraceResult(flags, buffer.SegmentCount, buffer.HitCount, travelDistance, endPosition);
        }

        private static TrajectoryTraceFlags TryAddSegment(
            DeterministicTrajectoryTraceBuffer buffer,
            FPVector3 from,
            FPVector3 to,
            FPVector3 direction,
            FPInt64 distance,
            int hitIndex)
        {
            var segment = new DeterministicTrajectorySegment(
                buffer.SegmentCount,
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
            out DeterministicTrajectoryHit selected)
        {
            selected = default;
            bool hasSelected = false;
            FPInt64 bestDistance = FPInt64.Zero;
            int limit = Math.Min(hitCount, hits.Length);

            for (int i = 0; i < limit; i++)
            {
                DeterministicTrajectoryHit hit = hits[i];
                if (!hit.IsValid)
                {
                    continue;
                }

                if (!hasSelected || hit.Distance < bestDistance || (hit.Distance == bestDistance && IsDeterministicTieBreak(hit, selected)))
                {
                    selected = hit;
                    bestDistance = hit.Distance;
                    hasSelected = true;
                }
            }

            return hasSelected;
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
            return value.SqrMagnitude.RawValue == 0 ? FPVector3.Zero : value.Normalized;
        }

        private static FPVector3 Reflect(FPVector3 vector, FPVector3 normal)
        {
            return vector - normal * (FPInt64.FromInt(2) * FPVector3.Dot(vector, normal));
        }
    }
}
