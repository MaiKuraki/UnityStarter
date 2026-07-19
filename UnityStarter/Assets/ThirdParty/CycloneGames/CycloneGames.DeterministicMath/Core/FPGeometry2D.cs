using System;
using System.Runtime.CompilerServices;

namespace CycloneGames.DeterministicMath
{
    /// <summary>
    /// Q32.32 two-dimensional geometry queries with value-type inputs and outputs.
    /// </summary>
    public static class FPGeometry2D
    {
        // ---- Circle-Circle ----

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CircleOverlap(FPCircle a, FPCircle b)
        {
            ulong radiusSumRaw = unchecked((ulong)a.Radius.RawValue + (ulong)b.Radius.RawValue);
            return FPMagnitudeUtility.IsDistanceWithin(a.Center, b.Center, radiusSumRaw);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CircleContainsPoint(FPCircle circle, FPVector2 point)
        {
            return FPMagnitudeUtility.IsDistanceWithin(
                circle.Center,
                point,
                (ulong)circle.Radius.RawValue);
        }

        // ---- AABB-AABB ----

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool AABBOverlap(FPAABB2D a, FPAABB2D b)
        {
            return a.Min.X <= b.Max.X && a.Max.X >= b.Min.X &&
                   a.Min.Y <= b.Max.Y && a.Max.Y >= b.Min.Y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool AABBContainsPoint(FPAABB2D aabb, FPVector2 point)
        {
            return point.X >= aabb.Min.X && point.X <= aabb.Max.X &&
                   point.Y >= aabb.Min.Y && point.Y <= aabb.Max.Y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool AABBContainsAABB(FPAABB2D outer, FPAABB2D inner)
        {
            return inner.Min.X >= outer.Min.X && inner.Max.X <= outer.Max.X &&
                   inner.Min.Y >= outer.Min.Y && inner.Max.Y <= outer.Max.Y;
        }

        // ---- Circle-AABB ----

        /// <summary>Tests overlap between a circle and an AABB.</summary>
        public static bool CircleAABBOverlap(FPCircle circle, FPAABB2D aabb)
        {
            var closest = ClosestPointOnAABB(aabb, circle.Center);
            return FPMagnitudeUtility.IsDistanceWithin(
                circle.Center,
                closest,
                (ulong)circle.Radius.RawValue);
        }

        // ---- Ray Casting ----

        /// <summary>
        /// Tries to intersect a ray with an AABB. A hit parameter satisfies
        /// point(t) = ray.Origin + ray.Direction * t.
        /// </summary>
        public static bool TryRayAABB(FPRay2D ray, FPAABB2D aabb, out FPInt64 t)
        {
            if (ray.Direction.X.RawValue == 0 && ray.Direction.Y.RawValue == 0)
            {
                t = default;
                return false;
            }

            bool initialized = false;
            FPInt64 tMin = default;
            FPInt64 tMax = default;
            if (!FPGeometryUtility.TryUpdateSlab(
                    ray.Origin.X,
                    ray.Direction.X,
                    aabb.Min.X,
                    aabb.Max.X,
                    ref initialized,
                    ref tMin,
                    ref tMax) ||
                !FPGeometryUtility.TryUpdateSlab(
                    ray.Origin.Y,
                    ray.Direction.Y,
                    aabb.Min.Y,
                    aabb.Max.Y,
                    ref initialized,
                    ref tMin,
                    ref tMax) ||
                !initialized ||
                tMax.RawValue < 0)
            {
                t = default;
                return false;
            }

            t = tMin.RawValue >= 0 ? tMin : tMax;
            return true;
        }

        /// <summary>
        /// Tries to intersect a ray with a circle using checked fixed-point intermediates. A hit parameter satisfies
        /// point(t) = ray.Origin + ray.Direction * t.
        /// </summary>
        public static bool TryRayCircle(FPRay2D ray, FPCircle circle, out FPInt64 t)
        {
            FPVector2 direction = ray.Direction;
            if (!FPInt64.TrySubtract(ray.Origin.X, circle.Center.X, out FPInt64 ocX) ||
                !FPInt64.TrySubtract(ray.Origin.Y, circle.Center.Y, out FPInt64 ocY) ||
                !FPMagnitudeUtility.TryDot(
                    direction.X,
                    direction.Y,
                    direction.X,
                    direction.Y,
                    out FPInt64 directionSqr) ||
                !FPMagnitudeUtility.TryDot(
                    ocX,
                    ocY,
                    direction.X,
                    direction.Y,
                    out FPInt64 halfB) ||
                !FPMagnitudeUtility.TryDot(ocX, ocY, ocX, ocY, out FPInt64 originDistanceSqr) ||
                !FPInt64.TryMultiply(circle.Radius, circle.Radius, out FPInt64 radiusSqr) ||
                !FPInt64.TrySubtract(originDistanceSqr, radiusSqr, out FPInt64 c))
            {
                t = default;
                return false;
            }

            return FPGeometryUtility.TrySolveRayQuadratic(directionSqr, halfB, c, out t);
        }

        // ---- Closest Point ----

        /// <summary>Closest point on AABB surface (or inside) to a given point.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector2 ClosestPointOnAABB(FPAABB2D aabb, FPVector2 point)
        {
            return new FPVector2(
                FPInt64.Clamp(point.X, aabb.Min.X, aabb.Max.X),
                FPInt64.Clamp(point.Y, aabb.Min.Y, aabb.Max.Y)
            );
        }

        /// <summary>Closest point on circle surface to a given point.</summary>
        public static FPVector2 ClosestPointOnCircle(FPCircle circle, FPVector2 point)
        {
            if (!TryClosestPointOnCircle(circle, point, out FPVector2 closestPoint))
            {
                throw new OverflowException("The closest point is outside the Q32.32 calculation domain.");
            }

            return closestPoint;
        }

        public static bool TryClosestPointOnCircle(FPCircle circle, FPVector2 point, out FPVector2 closestPoint)
        {
            if (!FPInt64.TrySubtract(point.X, circle.Center.X, out FPInt64 directionX) ||
                !FPInt64.TrySubtract(point.Y, circle.Center.Y, out FPInt64 directionY))
            {
                closestPoint = default;
                return false;
            }

            var direction = new FPVector2(directionX, directionY);
            if (!direction.TryNormalize(out FPVector2 normalized))
            {
                if (!FPInt64.TryAdd(circle.Center.X, circle.Radius, out FPInt64 x) &&
                    !FPInt64.TrySubtract(circle.Center.X, circle.Radius, out x))
                {
                    closestPoint = default;
                    return false;
                }

                closestPoint = new FPVector2(x, circle.Center.Y);
                return true;
            }

            if (!FPInt64.TryMultiply(normalized.X, circle.Radius, out FPInt64 offsetX) ||
                !FPInt64.TryMultiply(normalized.Y, circle.Radius, out FPInt64 offsetY) ||
                !FPInt64.TryAdd(circle.Center.X, offsetX, out FPInt64 closestX) ||
                !FPInt64.TryAdd(circle.Center.Y, offsetY, out FPInt64 closestY))
            {
                closestPoint = default;
                return false;
            }

            closestPoint = new FPVector2(closestX, closestY);
            return true;
        }

    }
}
