using System.Runtime.CompilerServices;

namespace CycloneGames.DeterministicMath
{
    internal static class FPGeometryUtility
    {
        private static readonly FPInt64 MaxValue = FPInt64.FromRaw(long.MaxValue);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static FPInt64 Midpoint(FPInt64 min, FPInt64 max)
        {
            long a = min.RawValue;
            long b = max.RawValue;
            return FPInt64.FromRaw((a & b) + ((a ^ b) >> 1));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static FPInt64 HalfDistance(FPInt64 min, FPInt64 max)
        {
            ulong distance = unchecked((ulong)max.RawValue - (ulong)min.RawValue);
            return FPInt64.FromRaw((long)(distance >> 1));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static FPInt64 DistanceSaturated(FPInt64 min, FPInt64 max)
        {
            ulong distance = unchecked((ulong)max.RawValue - (ulong)min.RawValue);
            return distance > (ulong)long.MaxValue
                ? MaxValue
                : FPInt64.FromRaw((long)distance);
        }

        internal static bool TrySolveRayQuadratic(
            FPInt64 directionSqr,
            FPInt64 halfB,
            FPInt64 c,
            out FPInt64 t)
        {
            const long MIN_DIRECTION_SQR_RAW = 100L;

            t = default;
            if (directionSqr.RawValue < MIN_DIRECTION_SQR_RAW ||
                !FPInt64.TryMultiply(halfB, halfB, out FPInt64 halfBSqr) ||
                !FPInt64.TryMultiply(directionSqr, c, out FPInt64 directionTimesC) ||
                !FPInt64.TrySubtract(halfBSqr, directionTimesC, out FPInt64 discriminant) ||
                discriminant.RawValue < 0 ||
                !FPInt64.TrySqrt(discriminant, out FPInt64 sqrtDiscriminant))
            {
                return false;
            }

            FPInt64 t0 = default;
            FPInt64 t1 = default;
            bool hasT0 = FPInt64.TryAdd(halfB, sqrtDiscriminant, out FPInt64 halfBPlusRoot) &&
                         FPInt64.TryNegate(halfBPlusRoot, out FPInt64 numerator0) &&
                         FPInt64.TryDivide(numerator0, directionSqr, out t0) &&
                         t0.RawValue >= 0;
            bool hasT1 = FPInt64.TrySubtract(sqrtDiscriminant, halfB, out FPInt64 numerator1) &&
                         FPInt64.TryDivide(numerator1, directionSqr, out t1) &&
                         t1.RawValue >= 0;

            if (hasT0 && (!hasT1 || t0 <= t1))
            {
                t = t0;
                return true;
            }

            if (hasT1)
            {
                t = t1;
                return true;
            }

            return false;
        }

        internal static bool TryUpdateSlab(
            FPInt64 origin,
            FPInt64 direction,
            FPInt64 min,
            FPInt64 max,
            ref bool initialized,
            ref FPInt64 tMin,
            ref FPInt64 tMax)
        {
            if (direction.RawValue == 0)
            {
                return origin >= min && origin <= max;
            }

            if (!FPInt64.TrySubtract(min, origin, out FPInt64 minDelta) ||
                !FPInt64.TrySubtract(max, origin, out FPInt64 maxDelta) ||
                !FPInt64.TryDivide(minDelta, direction, out FPInt64 t0) ||
                !FPInt64.TryDivide(maxDelta, direction, out FPInt64 t1))
            {
                return false;
            }

            if (t0 > t1)
            {
                FPInt64 swap = t0;
                t0 = t1;
                t1 = swap;
            }

            if (!initialized)
            {
                tMin = t0;
                tMax = t1;
                initialized = true;
                return true;
            }

            tMin = FPInt64.Max(tMin, t0);
            tMax = FPInt64.Min(tMax, t1);
            return tMin <= tMax;
        }
    }
}
