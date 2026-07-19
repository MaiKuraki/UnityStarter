using System;
using System.Runtime.CompilerServices;

namespace CycloneGames.DeterministicMath
{
    /// <summary>
    /// Q32.32 three-dimensional geometry queries with value-type inputs and outputs.
    /// </summary>
    public static class FPGeometry3D
    {
        // ---- Sphere ----

        /// <summary>Sphere vs point containment.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool SphereContainsPoint(FPSphere sphere, FPVector3 point) =>
            FPMagnitudeUtility.IsDistanceWithin(
                sphere.Center,
                point,
                (ulong)sphere.Radius.RawValue);

        /// <summary>Sphere vs sphere overlap.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool SphereOverlap(FPSphere a, FPSphere b)
        {
            ulong radiusSumRaw = unchecked((ulong)a.Radius.RawValue + (ulong)b.Radius.RawValue);
            return FPMagnitudeUtility.IsDistanceWithin(a.Center, b.Center, radiusSumRaw);
        }

        /// <summary>
        /// Tries to intersect a ray with a sphere using checked fixed-point intermediates. A hit parameter satisfies
        /// point(t) = ray.Origin + ray.Direction * t.
        /// </summary>
        public static bool TryRaySphere(FPRay3D ray, FPSphere sphere, out FPInt64 t)
        {
            FPVector3 direction = ray.Direction;
            if (!FPInt64.TrySubtract(ray.Origin.X, sphere.Center.X, out FPInt64 ocX) ||
                !FPInt64.TrySubtract(ray.Origin.Y, sphere.Center.Y, out FPInt64 ocY) ||
                !FPInt64.TrySubtract(ray.Origin.Z, sphere.Center.Z, out FPInt64 ocZ) ||
                !FPMagnitudeUtility.TryDot(
                    direction.X,
                    direction.Y,
                    direction.Z,
                    direction.X,
                    direction.Y,
                    direction.Z,
                    out FPInt64 directionSqr) ||
                !FPMagnitudeUtility.TryDot(
                    ocX,
                    ocY,
                    ocZ,
                    direction.X,
                    direction.Y,
                    direction.Z,
                    out FPInt64 halfB) ||
                !FPMagnitudeUtility.TryDot(ocX, ocY, ocZ, ocX, ocY, ocZ, out FPInt64 originDistanceSqr) ||
                !FPInt64.TryMultiply(sphere.Radius, sphere.Radius, out FPInt64 radiusSqr) ||
                !FPInt64.TrySubtract(originDistanceSqr, radiusSqr, out FPInt64 c))
            {
                t = default;
                return false;
            }

            return FPGeometryUtility.TrySolveRayQuadratic(directionSqr, halfB, c, out t);
        }

        // ---- AABB ----

        /// <summary>AABB vs point containment.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool AABBContainsPoint(FPAABB3D b, FPVector3 p) =>
            p.X >= b.Min.X && p.X <= b.Max.X &&
            p.Y >= b.Min.Y && p.Y <= b.Max.Y &&
            p.Z >= b.Min.Z && p.Z <= b.Max.Z;

        /// <summary>AABB vs AABB overlap.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool AABBOverlap(FPAABB3D a, FPAABB3D b) =>
            a.Min.X <= b.Max.X && a.Max.X >= b.Min.X &&
            a.Min.Y <= b.Max.Y && a.Max.Y >= b.Min.Y &&
            a.Min.Z <= b.Max.Z && a.Max.Z >= b.Min.Z;

        /// <summary>
        /// Tries to intersect a ray with an AABB. A hit parameter satisfies
        /// point(t) = ray.Origin + ray.Direction * t.
        /// </summary>
        public static bool TryRayAABB(FPRay3D ray, FPAABB3D bounds, out FPInt64 t)
        {
            FPVector3 direction = ray.Direction;
            if (direction.X.RawValue == 0 && direction.Y.RawValue == 0 && direction.Z.RawValue == 0)
            {
                t = default;
                return false;
            }

            bool initialized = false;
            FPInt64 tMin = default;
            FPInt64 tMax = default;
            if (!FPGeometryUtility.TryUpdateSlab(
                    ray.Origin.X,
                    direction.X,
                    bounds.Min.X,
                    bounds.Max.X,
                    ref initialized,
                    ref tMin,
                    ref tMax) ||
                !FPGeometryUtility.TryUpdateSlab(
                    ray.Origin.Y,
                    direction.Y,
                    bounds.Min.Y,
                    bounds.Max.Y,
                    ref initialized,
                    ref tMin,
                    ref tMax) ||
                !FPGeometryUtility.TryUpdateSlab(
                    ray.Origin.Z,
                    direction.Z,
                    bounds.Min.Z,
                    bounds.Max.Z,
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

        // ---- OBB (Separating Axis Theorem) ----

        /// <summary>
        /// Tries to intersect a ray with an OBB using checked coordinate translation. A hit parameter satisfies
        /// point(t) = ray.Origin + ray.Direction * t.
        /// </summary>
        public static bool TryRayOBB(FPRay3D ray, FPOBB3D obb, out FPInt64 t)
        {
            if (!obb.IsValid ||
                !FPInt64.TrySubtract(ray.Origin.X, obb.Center.X, out FPInt64 localX) ||
                !FPInt64.TrySubtract(ray.Origin.Y, obb.Center.Y, out FPInt64 localY) ||
                !FPInt64.TrySubtract(ray.Origin.Z, obb.Center.Z, out FPInt64 localZ))
            {
                t = default;
                return false;
            }

            // Transform ray into OBB local space
            var invOrientation = obb.Orientation.Conjugate;
            if (!FPQuaternion.TryRotateNormalized(
                    invOrientation,
                    new FPVector3(localX, localY, localZ),
                    out FPVector3 localOrigin) ||
                !FPQuaternion.TryRotateNormalized(invOrientation, ray.Direction, out FPVector3 localDir))
            {
                t = default;
                return false;
            }

            var localBounds = new FPAABB3D(-obb.HalfExtents, obb.HalfExtents);
            var localRay = new FPRay3D(localOrigin, localDir);
            return TryRayAABB(localRay, localBounds, out t);
        }

        /// <summary>OBB vs OBB overlap (Separating Axis Theorem with 15 axes).</summary>
        public static bool OBBOverlap(FPOBB3D a, FPOBB3D b)
        {
            if (!a.IsValid)
            {
                throw new ArgumentException("The first OBB is invalid.", nameof(a));
            }

            if (!b.IsValid)
            {
                throw new ArgumentException("The second OBB is invalid.", nameof(b));
            }

            // Get OBB axes: identity axes rotated by orientation
            var aX = a.Orientation * FPVector3.Right;
            var aY = a.Orientation * FPVector3.Up;
            var aZ = a.Orientation * FPVector3.Forward;

            var bX = b.Orientation * FPVector3.Right;
            var bY = b.Orientation * FPVector3.Up;
            var bZ = b.Orientation * FPVector3.Forward;

            // 15 separating axes: 3 from A, 3 from B, 9 from cross products
            return
                AxisOverlaps(aX, aX, aY, aZ, a.HalfExtents, bX, bY, bZ, b.HalfExtents, a.Center, b.Center) &&
                AxisOverlaps(aY, aX, aY, aZ, a.HalfExtents, bX, bY, bZ, b.HalfExtents, a.Center, b.Center) &&
                AxisOverlaps(aZ, aX, aY, aZ, a.HalfExtents, bX, bY, bZ, b.HalfExtents, a.Center, b.Center) &&
                AxisOverlaps(bX, aX, aY, aZ, a.HalfExtents, bX, bY, bZ, b.HalfExtents, a.Center, b.Center) &&
                AxisOverlaps(bY, aX, aY, aZ, a.HalfExtents, bX, bY, bZ, b.HalfExtents, a.Center, b.Center) &&
                AxisOverlaps(bZ, aX, aY, aZ, a.HalfExtents, bX, bY, bZ, b.HalfExtents, a.Center, b.Center) &&
                AxisOverlaps(FPVector3.Cross(aX, bX), aX, aY, aZ, a.HalfExtents, bX, bY, bZ, b.HalfExtents, a.Center, b.Center) &&
                AxisOverlaps(FPVector3.Cross(aX, bY), aX, aY, aZ, a.HalfExtents, bX, bY, bZ, b.HalfExtents, a.Center, b.Center) &&
                AxisOverlaps(FPVector3.Cross(aX, bZ), aX, aY, aZ, a.HalfExtents, bX, bY, bZ, b.HalfExtents, a.Center, b.Center) &&
                AxisOverlaps(FPVector3.Cross(aY, bX), aX, aY, aZ, a.HalfExtents, bX, bY, bZ, b.HalfExtents, a.Center, b.Center) &&
                AxisOverlaps(FPVector3.Cross(aY, bY), aX, aY, aZ, a.HalfExtents, bX, bY, bZ, b.HalfExtents, a.Center, b.Center) &&
                AxisOverlaps(FPVector3.Cross(aY, bZ), aX, aY, aZ, a.HalfExtents, bX, bY, bZ, b.HalfExtents, a.Center, b.Center) &&
                AxisOverlaps(FPVector3.Cross(aZ, bX), aX, aY, aZ, a.HalfExtents, bX, bY, bZ, b.HalfExtents, a.Center, b.Center) &&
                AxisOverlaps(FPVector3.Cross(aZ, bY), aX, aY, aZ, a.HalfExtents, bX, bY, bZ, b.HalfExtents, a.Center, b.Center) &&
                AxisOverlaps(FPVector3.Cross(aZ, bZ), aX, aY, aZ, a.HalfExtents, bX, bY, bZ, b.HalfExtents, a.Center, b.Center);
        }

        private static bool AxisOverlaps(
            in FPVector3 axis,
            in FPVector3 ax,
            in FPVector3 ay,
            in FPVector3 az,
            in FPVector3 halfA,
            in FPVector3 bx,
            in FPVector3 by,
            in FPVector3 bz,
            in FPVector3 halfB,
            in FPVector3 centerA,
            in FPVector3 centerB)
        {
            if (axis.SqrMagnitude.RawValue < 100L)
            {
                return true;
            }

            if (!TryProjectionRadius(axis, ax, ay, az, halfA, out ulong projectionAHigh, out ulong projectionALow) ||
                !TryProjectionRadius(axis, bx, by, bz, halfB, out ulong projectionBHigh, out ulong projectionBLow) ||
                !TryAddUnsigned128(
                    ref projectionAHigh,
                    ref projectionALow,
                    projectionBHigh,
                    projectionBLow) ||
                !TryGetAbsoluteCenterProjection(
                    axis,
                    centerA,
                    centerB,
                    out ulong centerDistanceHigh,
                    out ulong centerDistanceLow))
            {
                return false;
            }

            return CompareUnsigned128(
                centerDistanceHigh,
                centerDistanceLow,
                projectionAHigh,
                projectionALow) <= 0;
        }

        private static bool TryProjectionRadius(
            in FPVector3 axis,
            in FPVector3 xAxis,
            in FPVector3 yAxis,
            in FPVector3 zAxis,
            in FPVector3 halfExtents,
            out ulong projectionHigh,
            out ulong projectionLow)
        {
            if (!FPMagnitudeUtility.TryDot(axis, xAxis, out FPInt64 signedX) ||
                !FPMagnitudeUtility.TryDot(axis, yAxis, out FPInt64 signedY) ||
                !FPMagnitudeUtility.TryDot(axis, zAxis, out FPInt64 signedZ))
            {
                projectionHigh = 0;
                projectionLow = 0;
                return false;
            }

            projectionHigh = 0;
            projectionLow = 0;
            return TryAccumulateUnsignedProduct(
                       AbsRaw(signedX.RawValue),
                       (ulong)halfExtents.X.RawValue,
                       ref projectionHigh,
                       ref projectionLow) &&
                   TryAccumulateUnsignedProduct(
                       AbsRaw(signedY.RawValue),
                       (ulong)halfExtents.Y.RawValue,
                       ref projectionHigh,
                       ref projectionLow) &&
                   TryAccumulateUnsignedProduct(
                       AbsRaw(signedZ.RawValue),
                       (ulong)halfExtents.Z.RawValue,
                       ref projectionHigh,
                       ref projectionLow);
        }

        private static bool TryGetAbsoluteCenterProjection(
            in FPVector3 axis,
            in FPVector3 centerA,
            in FPVector3 centerB,
            out ulong projectionHigh,
            out ulong projectionLow)
        {
            bool isNegative = false;
            projectionHigh = 0;
            projectionLow = 0;

            return TryAccumulateSignedDifferenceProduct(
                       axis.X.RawValue,
                       centerA.X.RawValue,
                       centerB.X.RawValue,
                       ref isNegative,
                       ref projectionHigh,
                       ref projectionLow) &&
                   TryAccumulateSignedDifferenceProduct(
                       axis.Y.RawValue,
                       centerA.Y.RawValue,
                       centerB.Y.RawValue,
                       ref isNegative,
                       ref projectionHigh,
                       ref projectionLow) &&
                   TryAccumulateSignedDifferenceProduct(
                       axis.Z.RawValue,
                       centerA.Z.RawValue,
                       centerB.Z.RawValue,
                       ref isNegative,
                       ref projectionHigh,
                       ref projectionLow);
        }

        private static bool TryAccumulateSignedDifferenceProduct(
            long axisRaw,
            long centerARaw,
            long centerBRaw,
            ref bool accumulatorIsNegative,
            ref ulong accumulatorHigh,
            ref ulong accumulatorLow)
        {
            MultiplyUnsigned64(
                AbsRaw(axisRaw),
                AbsDifferenceRaw(centerARaw, centerBRaw),
                out ulong productHigh,
                out ulong productLow);
            ShiftRight32(productHigh, productLow, out ulong termHigh, out ulong termLow);
            bool termIsNegative = (axisRaw < 0) ^ (centerBRaw < centerARaw);
            return TryAccumulateSigned128(
                termIsNegative,
                termHigh,
                termLow,
                ref accumulatorIsNegative,
                ref accumulatorHigh,
                ref accumulatorLow);
        }

        private static bool TryAccumulateUnsignedProduct(
            ulong a,
            ulong b,
            ref ulong accumulatorHigh,
            ref ulong accumulatorLow)
        {
            MultiplyUnsigned64(a, b, out ulong productHigh, out ulong productLow);
            ShiftRight32(productHigh, productLow, out ulong termHigh, out ulong termLow);
            return TryAddUnsigned128(
                ref accumulatorHigh,
                ref accumulatorLow,
                termHigh,
                termLow);
        }

        private static bool TryAccumulateSigned128(
            bool termIsNegative,
            ulong termHigh,
            ulong termLow,
            ref bool accumulatorIsNegative,
            ref ulong accumulatorHigh,
            ref ulong accumulatorLow)
        {
            if ((termHigh | termLow) == 0)
            {
                return true;
            }

            if ((accumulatorHigh | accumulatorLow) == 0)
            {
                accumulatorIsNegative = termIsNegative;
                accumulatorHigh = termHigh;
                accumulatorLow = termLow;
                return true;
            }

            if (accumulatorIsNegative == termIsNegative)
            {
                return TryAddUnsigned128(
                    ref accumulatorHigh,
                    ref accumulatorLow,
                    termHigh,
                    termLow);
            }

            int comparison = CompareUnsigned128(
                accumulatorHigh,
                accumulatorLow,
                termHigh,
                termLow);
            if (comparison == 0)
            {
                accumulatorIsNegative = false;
                accumulatorHigh = 0;
                accumulatorLow = 0;
                return true;
            }

            if (comparison > 0)
            {
                SubtractUnsigned128(
                    accumulatorHigh,
                    accumulatorLow,
                    termHigh,
                    termLow,
                    out accumulatorHigh,
                    out accumulatorLow);
                return true;
            }

            SubtractUnsigned128(
                termHigh,
                termLow,
                accumulatorHigh,
                accumulatorLow,
                out accumulatorHigh,
                out accumulatorLow);
            accumulatorIsNegative = termIsNegative;
            return true;
        }

        private static bool TryAddUnsigned128(
            ref ulong accumulatorHigh,
            ref ulong accumulatorLow,
            ulong addendHigh,
            ulong addendLow)
        {
            ulong newLow = unchecked(accumulatorLow + addendLow);
            ulong carry = newLow < accumulatorLow ? 1UL : 0UL;
            if (accumulatorHigh > ulong.MaxValue - addendHigh)
            {
                return false;
            }

            ulong newHigh = accumulatorHigh + addendHigh;
            if (newHigh > ulong.MaxValue - carry)
            {
                return false;
            }

            accumulatorHigh = newHigh + carry;
            accumulatorLow = newLow;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CompareUnsigned128(
            ulong leftHigh,
            ulong leftLow,
            ulong rightHigh,
            ulong rightLow)
        {
            if (leftHigh != rightHigh)
            {
                return leftHigh < rightHigh ? -1 : 1;
            }

            if (leftLow == rightLow)
            {
                return 0;
            }

            return leftLow < rightLow ? -1 : 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SubtractUnsigned128(
            ulong leftHigh,
            ulong leftLow,
            ulong rightHigh,
            ulong rightLow,
            out ulong resultHigh,
            out ulong resultLow)
        {
            ulong borrow = leftLow < rightLow ? 1UL : 0UL;
            resultLow = unchecked(leftLow - rightLow);
            resultHigh = leftHigh - rightHigh - borrow;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ShiftRight32(
            ulong high,
            ulong low,
            out ulong resultHigh,
            out ulong resultLow)
        {
            resultHigh = high >> FPInt64.FractionalBits;
            resultLow = (high << FPInt64.FractionalBits) | (low >> FPInt64.FractionalBits);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong AbsRaw(long raw) =>
            raw < 0 ? (ulong)(-(raw + 1)) + 1UL : (ulong)raw;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong AbsDifferenceRaw(long a, long b)
        {
            ulong orderedA = unchecked((ulong)a) ^ 0x8000000000000000UL;
            ulong orderedB = unchecked((ulong)b) ^ 0x8000000000000000UL;
            return orderedA >= orderedB ? orderedA - orderedB : orderedB - orderedA;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void MultiplyUnsigned64(ulong a, ulong b, out ulong high, out ulong low)
        {
            const ulong MASK_32 = 0xFFFFFFFFUL;

            ulong aLow = a & MASK_32;
            ulong aHigh = a >> FPInt64.FractionalBits;
            ulong bLow = b & MASK_32;
            ulong bHigh = b >> FPInt64.FractionalBits;

            ulong lowLow = aLow * bLow;
            ulong lowHigh = aLow * bHigh;
            ulong highLow = aHigh * bLow;
            ulong highHigh = aHigh * bHigh;

            ulong middle = (lowLow >> FPInt64.FractionalBits) +
                           (lowHigh & MASK_32) +
                           (highLow & MASK_32);
            low = (middle << FPInt64.FractionalBits) | (lowLow & MASK_32);
            high = highHigh +
                   (lowHigh >> FPInt64.FractionalBits) +
                   (highLow >> FPInt64.FractionalBits) +
                   (middle >> FPInt64.FractionalBits);
        }

        // ---- Closest Point ----

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector3 ClosestPointOnAABB(FPAABB3D b, FPVector3 p) =>
            new FPVector3(
                FPInt64.Clamp(p.X, b.Min.X, b.Max.X),
                FPInt64.Clamp(p.Y, b.Min.Y, b.Max.Y),
                FPInt64.Clamp(p.Z, b.Min.Z, b.Max.Z));

        public static FPVector3 ClosestPointOnSphere(FPSphere s, FPVector3 p)
        {
            if (!TryClosestPointOnSphere(s, p, out FPVector3 closestPoint))
            {
                throw new OverflowException("The closest point is outside the Q32.32 calculation domain.");
            }

            return closestPoint;
        }

        public static bool TryClosestPointOnSphere(FPSphere sphere, FPVector3 point, out FPVector3 closestPoint)
        {
            if (!FPInt64.TrySubtract(point.X, sphere.Center.X, out FPInt64 directionX) ||
                !FPInt64.TrySubtract(point.Y, sphere.Center.Y, out FPInt64 directionY) ||
                !FPInt64.TrySubtract(point.Z, sphere.Center.Z, out FPInt64 directionZ))
            {
                closestPoint = default;
                return false;
            }

            var direction = new FPVector3(directionX, directionY, directionZ);
            if (!direction.TryNormalize(out FPVector3 normalized))
            {
                if (!FPInt64.TryAdd(sphere.Center.X, sphere.Radius, out FPInt64 x) &&
                    !FPInt64.TrySubtract(sphere.Center.X, sphere.Radius, out x))
                {
                    closestPoint = default;
                    return false;
                }

                closestPoint = new FPVector3(x, sphere.Center.Y, sphere.Center.Z);
                return true;
            }

            if (!FPInt64.TryMultiply(normalized.X, sphere.Radius, out FPInt64 offsetX) ||
                !FPInt64.TryMultiply(normalized.Y, sphere.Radius, out FPInt64 offsetY) ||
                !FPInt64.TryMultiply(normalized.Z, sphere.Radius, out FPInt64 offsetZ) ||
                !FPInt64.TryAdd(sphere.Center.X, offsetX, out FPInt64 closestX) ||
                !FPInt64.TryAdd(sphere.Center.Y, offsetY, out FPInt64 closestY) ||
                !FPInt64.TryAdd(sphere.Center.Z, offsetZ, out FPInt64 closestZ))
            {
                closestPoint = default;
                return false;
            }

            closestPoint = new FPVector3(closestX, closestY, closestZ);
            return true;
        }

        /// <summary>Sphere vs AABB overlap.</summary>
        public static bool SphereAABBOverlap(FPSphere sphere, FPAABB3D bounds)
        {
            var closest = ClosestPointOnAABB(bounds, sphere.Center);
            return FPMagnitudeUtility.IsDistanceWithin(
                sphere.Center,
                closest,
                (ulong)sphere.Radius.RawValue);
        }
    }
}
