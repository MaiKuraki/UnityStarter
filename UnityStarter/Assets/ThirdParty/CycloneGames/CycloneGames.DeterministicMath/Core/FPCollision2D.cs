using System;
using System.Runtime.CompilerServices;

namespace CycloneGames.DeterministicMath
{
    // ---- Shape Definitions ----

    /// <summary>Deterministic 2D circle.</summary>
    public readonly struct FPCircle
    {
        public readonly FPVector2 Center;
        public readonly FPInt64 Radius;

        public FPCircle(FPVector2 center, FPInt64 radius) { Center = center; Radius = radius; }
    }

    /// <summary>Deterministic 2D axis-aligned bounding box.</summary>
    public readonly struct FPAABB
    {
        public readonly FPVector2 Min;
        public readonly FPVector2 Max;

        public FPAABB(FPVector2 min, FPVector2 max) { Min = min; Max = max; }

        public FPVector2 Center => (Min + Max) / 2;
        public FPVector2 Extents => (Max - Min) / 2;
    }

    /// <summary>Deterministic 2D ray.</summary>
    public readonly struct FPRay2D
    {
        public readonly FPVector2 Origin;
        public readonly FPVector2 Direction;

        public FPRay2D(FPVector2 origin, FPVector2 direction)
        {
            Origin = origin;
            Direction = direction;
        }
    }

    /// <summary>
    /// Deterministic 2D collision detection.
    /// All tests are static, zero-allocation, and cross-platform bit-identical.
    /// </summary>
    public static class FPCollision2D
    {
        // ---- Circle-Circle ----

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CircleOverlap(FPCircle a, FPCircle b)
        {
            var radiusSum = a.Radius + b.Radius;
            var distSqr = FPVector2.DistanceSqr(a.Center, b.Center);
            return distSqr <= radiusSum * radiusSum;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool CircleContainsPoint(FPCircle circle, FPVector2 point)
        {
            return FPVector2.DistanceSqr(circle.Center, point) <= circle.Radius * circle.Radius;
        }

        // ---- AABB-AABB ----

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool AABBOverlap(FPAABB a, FPAABB b)
        {
            return a.Min.X <= b.Max.X && a.Max.X >= b.Min.X &&
                   a.Min.Y <= b.Max.Y && a.Max.Y >= b.Min.Y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool AABBContainsPoint(FPAABB aabb, FPVector2 point)
        {
            return point.X >= aabb.Min.X && point.X <= aabb.Max.X &&
                   point.Y >= aabb.Min.Y && point.Y <= aabb.Max.Y;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool AABBContainsAABB(FPAABB outer, FPAABB inner)
        {
            return inner.Min.X >= outer.Min.X && inner.Max.X <= outer.Max.X &&
                   inner.Min.Y >= outer.Min.Y && inner.Max.Y <= outer.Max.Y;
        }

        // ---- Circle-AABB ----

        /// <summary>Tests overlap between a circle and an AABB.</summary>
        public static bool CircleAABBOverlap(FPCircle circle, FPAABB aabb)
        {
            var closest = ClosestPointOnAABB(aabb, circle.Center);
            var distSqr = FPVector2.DistanceSqr(circle.Center, closest);
            return distSqr <= circle.Radius * circle.Radius;
        }

        // ---- Ray Casting ----

        /// <summary>
        /// Ray vs AABB intersection (slab method).
        /// Returns the intersection distance t along the ray, or -1 if no hit.
        /// </summary>
        public static FPInt64 RayAABBIntersect(FPRay2D ray, FPAABB aabb)
        {
            var invD = ray.Direction;
            if (invD.X.RawValue == 0 && invD.Y.RawValue == 0)
                return FPInt64.MinusOne;

            FPInt64 tMin = -FPInt64.OneValue; // Using large negative instead of actual -inf
            // For robust range, use MinValue for -inf proxy
            tMin = FPInt64.FromRaw(long.MinValue / 2);
            FPInt64 tMax = FPInt64.FromRaw(long.MaxValue / 2);

            // X axis
            if (invD.X.RawValue != 0)
            {
                var invX = FPInt64.OneValue / ray.Direction.X;
                var t0 = (aabb.Min.X - ray.Origin.X) * invX;
                var t1 = (aabb.Max.X - ray.Origin.X) * invX;
                if (invX.RawValue < 0) { var tmp = t0; t0 = t1; t1 = tmp; }
                tMin = FPInt64.Max(tMin, t0);
                tMax = FPInt64.Min(tMax, t1);
                if (tMin.RawValue > tMax.RawValue) return FPInt64.MinusOne;
            }
            else
            {
                // Ray is parallel to X axis - check if origin X is within slab
                if (ray.Origin.X < aabb.Min.X || ray.Origin.X > aabb.Max.X)
                    return FPInt64.MinusOne;
            }

            // Y axis
            if (invD.Y.RawValue != 0)
            {
                var invY = FPInt64.OneValue / ray.Direction.Y;
                var t0 = (aabb.Min.Y - ray.Origin.Y) * invY;
                var t1 = (aabb.Max.Y - ray.Origin.Y) * invY;
                if (invY.RawValue < 0) { var tmp = t0; t0 = t1; t1 = tmp; }
                tMin = FPInt64.Max(tMin, t0);
                tMax = FPInt64.Min(tMax, t1);
                if (tMin.RawValue > tMax.RawValue) return FPInt64.MinusOne;
            }
            else
            {
                if (ray.Origin.Y < aabb.Min.Y || ray.Origin.Y > aabb.Max.Y)
                    return FPInt64.MinusOne;
            }

            // Check if intersection is behind the ray
            if (tMax.RawValue < 0) return FPInt64.MinusOne;

            return tMin.RawValue >= 0 ? tMin : tMax;
        }

        /// <summary>
        /// Ray vs Circle intersection.
        /// Returns the nearest intersection distance t along the ray, or -1 if no hit.
        /// </summary>
        public static FPInt64 RayCircleIntersect(FPRay2D ray, FPCircle circle)
        {
            var d = ray.Direction;
            if (d.X.RawValue == 0 && d.Y.RawValue == 0)
                return FPInt64.MinusOne;

            var oc = ray.Origin - circle.Center;
            var a = FPVector2.Dot(d, d);
            var halfB = FPVector2.Dot(oc, d);
            var c = FPVector2.Dot(oc, oc) - circle.Radius * circle.Radius;

            // discriminant = halfB^2 - a*c
            var discriminant = halfB * halfB - a * c;

            if (discriminant.RawValue < 0) return FPInt64.MinusOne;

            var sqrtD = FPInt64.Sqrt(discriminant);

            // t = (-b +/- sqrt(b^2 - 4ac)) / (2a) but we use halfB = b/2
            // t = (-halfB +/- sqrt(halfB^2 - a*c)) / a
            var t0 = (-halfB - sqrtD) / a;
            var t1 = (-halfB + sqrtD) / a;

            if (t0.RawValue >= 0) return t0;
            if (t1.RawValue >= 0) return t1;
            return FPInt64.MinusOne;
        }

        // ---- Closest Point ----

        /// <summary>Closest point on AABB surface (or inside) to a given point.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector2 ClosestPointOnAABB(FPAABB aabb, FPVector2 point)
        {
            return new FPVector2(
                FPInt64.Clamp(point.X, aabb.Min.X, aabb.Max.X),
                FPInt64.Clamp(point.Y, aabb.Min.Y, aabb.Max.Y)
            );
        }

        /// <summary>Closest point on circle surface to a given point.</summary>
        public static FPVector2 ClosestPointOnCircle(FPCircle circle, FPVector2 point)
        {
            var dir = point - circle.Center;
            var distSqr = dir.SqrMagnitude;
            if (distSqr.RawValue == 0)
            {
                // Point is at center - any point on circumference is equally close
                return circle.Center + new FPVector2(circle.Radius, FPInt64.Zero);
            }
            var dirNorm = dir / FPInt64.Sqrt(distSqr);
            return circle.Center + dirNorm * circle.Radius;
        }
    }
}
