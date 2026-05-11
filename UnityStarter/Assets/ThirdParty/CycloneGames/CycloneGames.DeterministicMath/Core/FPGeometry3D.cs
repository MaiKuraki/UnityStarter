using System;
using System.Runtime.CompilerServices;

namespace CycloneGames.DeterministicMath
{
    // ---- 3D Shape Definitions ----

    /// <summary>Deterministic 3D sphere.</summary>
    public readonly struct FPSphere
    {
        public readonly FPVector3 Center;
        public readonly FPInt64 Radius;
        public FPSphere(FPVector3 center, FPInt64 radius) { Center = center; Radius = radius; }
    }

    /// <summary>Deterministic 3D axis-aligned bounding box.</summary>
    public readonly struct FPBounds
    {
        public readonly FPVector3 Min;
        public readonly FPVector3 Max;
        public FPBounds(FPVector3 min, FPVector3 max) { Min = min; Max = max; }
        public FPVector3 Center => (Min + Max) / 2;
        public FPVector3 Extents => (Max - Min) / 2;
        public FPVector3 Size => Max - Min;
    }

    /// <summary>Deterministic 3D oriented bounding box.</summary>
    public readonly struct FPOBB
    {
        public readonly FPVector3 Center;
        public readonly FPVector3 HalfExtents;
        public readonly FPQuaternion Orientation;
        public FPOBB(FPVector3 center, FPVector3 halfExtents, FPQuaternion orientation)
        { Center = center; HalfExtents = halfExtents; Orientation = orientation; }
    }

    /// <summary>Deterministic 3D ray.</summary>
    public readonly struct FPRay
    {
        public readonly FPVector3 Origin;
        public readonly FPVector3 Direction;
        public FPRay(FPVector3 origin, FPVector3 direction)
        { Origin = origin; Direction = direction; }
    }

    /// <summary>
    /// Deterministic 3D collision queries.
    /// All tests are static, zero-allocation, and cross-platform bit-identical.
    /// </summary>
    public static class FPRaycast3D
    {
        // ---- Sphere ----

        /// <summary>Sphere vs point containment.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool SphereContainsPoint(FPSphere sphere, FPVector3 point) =>
            FPVector3.DistanceSqr(sphere.Center, point) <= sphere.Radius * sphere.Radius;

        /// <summary>Sphere vs sphere overlap.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool SphereOverlap(FPSphere a, FPSphere b)
        {
            var rSum = a.Radius + b.Radius;
            return FPVector3.DistanceSqr(a.Center, b.Center) <= rSum * rSum;
        }

        /// <summary>Ray vs sphere intersection. Returns nearest t or -1 if no hit.</summary>
        public static FPInt64 RaySphere(FPRay ray, FPSphere sphere)
        {
            var oc = ray.Origin - sphere.Center;
            var a = FPVector3.Dot(ray.Direction, ray.Direction);
            var halfB = FPVector3.Dot(oc, ray.Direction);
            var c = FPVector3.Dot(oc, oc) - sphere.Radius * sphere.Radius;

            var discriminant = halfB * halfB - a * c;
            if (discriminant.RawValue < 0) return FPInt64.MinusOne;

            var sqrtD = FPInt64.Sqrt(discriminant);
            var t0 = (-halfB - sqrtD) / a;
            var t1 = (-halfB + sqrtD) / a;

            if (t0.RawValue >= 0) return t0;
            if (t1.RawValue >= 0) return t1;
            return FPInt64.MinusOne;
        }

        // ---- AABB ----

        /// <summary>AABB vs point containment.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool BoundsContainsPoint(FPBounds b, FPVector3 p) =>
            p.X >= b.Min.X && p.X <= b.Max.X &&
            p.Y >= b.Min.Y && p.Y <= b.Max.Y &&
            p.Z >= b.Min.Z && p.Z <= b.Max.Z;

        /// <summary>AABB vs AABB overlap.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool BoundsOverlap(FPBounds a, FPBounds b) =>
            a.Min.X <= b.Max.X && a.Max.X >= b.Min.X &&
            a.Min.Y <= b.Max.Y && a.Max.Y >= b.Min.Y &&
            a.Min.Z <= b.Max.Z && a.Max.Z >= b.Min.Z;

        /// <summary>Ray vs AABB intersection (slab method). Returns nearest t or -1 if no hit.</summary>
        public static FPInt64 RayBounds(FPRay ray, FPBounds bounds)
        {
            var d = ray.Direction;
            if (d.X.RawValue == 0 && d.Y.RawValue == 0 && d.Z.RawValue == 0)
                return FPInt64.MinusOne;

            FPInt64 tMin = FPInt64.FromRaw(long.MinValue >> 2); // safe "negative large"
            FPInt64 tMax = FPInt64.FromRaw(long.MaxValue >> 2); // safe "positive large"

            // X axis
            if (d.X.RawValue != 0)
            {
                var inv = FPInt64.OneValue / d.X;
                var t0 = (bounds.Min.X - ray.Origin.X) * inv;
                var t1 = (bounds.Max.X - ray.Origin.X) * inv;
                if (inv.RawValue < 0) { var tmp = t0; t0 = t1; t1 = tmp; }
                tMin = FPInt64.Max(tMin, t0);
                tMax = FPInt64.Min(tMax, t1);
                if (tMin.RawValue > tMax.RawValue) return FPInt64.MinusOne;
            }
            else if (ray.Origin.X < bounds.Min.X || ray.Origin.X > bounds.Max.X)
                return FPInt64.MinusOne;

            // Y axis
            if (d.Y.RawValue != 0)
            {
                var inv = FPInt64.OneValue / d.Y;
                var t0 = (bounds.Min.Y - ray.Origin.Y) * inv;
                var t1 = (bounds.Max.Y - ray.Origin.Y) * inv;
                if (inv.RawValue < 0) { var tmp = t0; t0 = t1; t1 = tmp; }
                tMin = FPInt64.Max(tMin, t0);
                tMax = FPInt64.Min(tMax, t1);
                if (tMin.RawValue > tMax.RawValue) return FPInt64.MinusOne;
            }
            else if (ray.Origin.Y < bounds.Min.Y || ray.Origin.Y > bounds.Max.Y)
                return FPInt64.MinusOne;

            // Z axis
            if (d.Z.RawValue != 0)
            {
                var inv = FPInt64.OneValue / d.Z;
                var t0 = (bounds.Min.Z - ray.Origin.Z) * inv;
                var t1 = (bounds.Max.Z - ray.Origin.Z) * inv;
                if (inv.RawValue < 0) { var tmp = t0; t0 = t1; t1 = tmp; }
                tMin = FPInt64.Max(tMin, t0);
                tMax = FPInt64.Min(tMax, t1);
                if (tMin.RawValue > tMax.RawValue) return FPInt64.MinusOne;
            }
            else if (ray.Origin.Z < bounds.Min.Z || ray.Origin.Z > bounds.Max.Z)
                return FPInt64.MinusOne;

            if (tMax.RawValue < 0) return FPInt64.MinusOne;
            return tMin.RawValue >= 0 ? tMin : tMax;
        }

        // ---- OBB (Separating Axis Theorem) ----

        /// <summary>Ray vs OBB intersection. Returns t or -1 if no hit.</summary>
        public static FPInt64 RayOBB(FPRay ray, FPOBB obb)
        {
            // Transform ray into OBB local space
            var invOrientation = obb.Orientation.Conjugate;
            var localOrigin = invOrientation * (ray.Origin - obb.Center);
            var localDir = invOrientation * ray.Direction;

            var localBounds = new FPBounds(-obb.HalfExtents, obb.HalfExtents);
            var localRay = new FPRay(localOrigin, localDir);
            return RayBounds(localRay, localBounds);
        }

        /// <summary>OBB vs OBB overlap (Separating Axis Theorem with 15 axes).</summary>
        public static bool OBBOverlap(FPOBB a, FPOBB b)
        {
            // Get OBB axes: identity axes rotated by orientation
            var aX = a.Orientation * FPVector3.Right;
            var aY = a.Orientation * FPVector3.Up;
            var aZ = a.Orientation * FPVector3.Forward;

            var bX = b.Orientation * FPVector3.Right;
            var bY = b.Orientation * FPVector3.Up;
            var bZ = b.Orientation * FPVector3.Forward;

            var t = b.Center - a.Center;

            // 15 separating axes: 3 from A, 3 from B, 9 from cross products
            return
                SATTest(aX, a.HalfExtents, bX, bY, bZ, b.HalfExtents, t) &&
                SATTest(aY, a.HalfExtents, bX, bY, bZ, b.HalfExtents, t) &&
                SATTest(aZ, a.HalfExtents, bX, bY, bZ, b.HalfExtents, t) &&
                SATTest(bX, b.HalfExtents, aX, aY, aZ, a.HalfExtents, -t) &&
                SATTest(bY, b.HalfExtents, aX, aY, aZ, a.HalfExtents, -t) &&
                SATTest(bZ, b.HalfExtents, aX, aY, aZ, a.HalfExtents, -t) &&
                SATCrossTest(aX, aY, aZ, a.HalfExtents, bX, bY, bZ, b.HalfExtents, t);
        }

        private static bool SATTest(FPVector3 axis, FPVector3 halfA,
            FPVector3 bx, FPVector3 by, FPVector3 bz, FPVector3 halfB, FPVector3 t)
        {
            var projA = FPInt64.Abs(FPVector3.Dot(axis, FPVector3.Right) * halfA.X) +
                         FPInt64.Abs(FPVector3.Dot(axis, FPVector3.Up) * halfA.Y) +
                         FPInt64.Abs(FPVector3.Dot(axis, FPVector3.Forward) * halfA.Z);

            var projB = FPInt64.Abs(FPVector3.Dot(axis, bx)) * halfB.X +
                         FPInt64.Abs(FPVector3.Dot(axis, by)) * halfB.Y +
                         FPInt64.Abs(FPVector3.Dot(axis, bz)) * halfB.Z;

            var dist = FPInt64.Abs(FPVector3.Dot(axis, t));
            return dist <= projA + projB;
        }

        private static bool SATCrossTest(
            FPVector3 ax, FPVector3 ay, FPVector3 az, FPVector3 ha,
            FPVector3 bx, FPVector3 by, FPVector3 bz, FPVector3 hb, FPVector3 t)
        {
            // Cross product axes: A[i] x B[j] for all 9 combinations.
            return SATCrossAxis(FPVector3.Cross(ax, bx), ax, ay, az, ha, bx, by, bz, hb, t) &&
                   SATCrossAxis(FPVector3.Cross(ax, by), ax, ay, az, ha, bx, by, bz, hb, t) &&
                   SATCrossAxis(FPVector3.Cross(ax, bz), ax, ay, az, ha, bx, by, bz, hb, t) &&
                   SATCrossAxis(FPVector3.Cross(ay, bx), ax, ay, az, ha, bx, by, bz, hb, t) &&
                   SATCrossAxis(FPVector3.Cross(ay, by), ax, ay, az, ha, bx, by, bz, hb, t) &&
                   SATCrossAxis(FPVector3.Cross(ay, bz), ax, ay, az, ha, bx, by, bz, hb, t) &&
                   SATCrossAxis(FPVector3.Cross(az, bx), ax, ay, az, ha, bx, by, bz, hb, t) &&
                   SATCrossAxis(FPVector3.Cross(az, by), ax, ay, az, ha, bx, by, bz, hb, t) &&
                   SATCrossAxis(FPVector3.Cross(az, bz), ax, ay, az, ha, bx, by, bz, hb, t);
        }

        private static bool SATCrossAxis(
            FPVector3 axis,
            FPVector3 ax, FPVector3 ay, FPVector3 az, FPVector3 ha,
            FPVector3 bx, FPVector3 by, FPVector3 bz, FPVector3 hb, FPVector3 t)
        {
            if (axis.SqrMagnitude.RawValue < 100L) return true;

            var projA = FPInt64.Abs(FPVector3.Dot(axis, ax)) * ha.X +
                         FPInt64.Abs(FPVector3.Dot(axis, ay)) * ha.Y +
                         FPInt64.Abs(FPVector3.Dot(axis, az)) * ha.Z;

            var projB = FPInt64.Abs(FPVector3.Dot(axis, bx)) * hb.X +
                         FPInt64.Abs(FPVector3.Dot(axis, by)) * hb.Y +
                         FPInt64.Abs(FPVector3.Dot(axis, bz)) * hb.Z;

            var dist = FPInt64.Abs(FPVector3.Dot(axis, t));
            return dist.RawValue <= (projA + projB).RawValue;
        }

        // ---- Closest Point ----

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector3 ClosestPointOnBounds(FPBounds b, FPVector3 p) =>
            new FPVector3(
                FPInt64.Clamp(p.X, b.Min.X, b.Max.X),
                FPInt64.Clamp(p.Y, b.Min.Y, b.Max.Y),
                FPInt64.Clamp(p.Z, b.Min.Z, b.Max.Z));

        public static FPVector3 ClosestPointOnSphere(FPSphere s, FPVector3 p)
        {
            var dir = p - s.Center;
            var sqrMag = dir.SqrMagnitude;
            if (sqrMag.RawValue == 0)
                return s.Center + new FPVector3(s.Radius, FPInt64.Zero, FPInt64.Zero);
            return s.Center + (dir / FPInt64.Sqrt(sqrMag)) * s.Radius;
        }

        /// <summary>Sphere vs AABB overlap.</summary>
        public static bool SphereBoundsOverlap(FPSphere sphere, FPBounds bounds)
        {
            var closest = ClosestPointOnBounds(bounds, sphere.Center);
            return FPVector3.DistanceSqr(sphere.Center, closest) <= sphere.Radius * sphere.Radius;
        }
    }
}
