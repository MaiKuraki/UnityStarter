using System.Runtime.CompilerServices;

namespace CycloneGames.DeterministicMath
{
    internal static class FPMagnitudeUtility
    {
        private static readonly FPInt64 MaxValue = FPInt64.FromRaw(long.MaxValue);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static FPInt64 GetSaturatedSquaredMagnitude(FPInt64 x, FPInt64 y)
        {
            return TryGetSquaredMagnitudeRaw(x.RawValue, y.RawValue, out long raw)
                ? FPInt64.FromRaw(raw)
                : MaxValue;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static FPInt64 GetSaturatedSquaredMagnitude(FPInt64 x, FPInt64 y, FPInt64 z)
        {
            return TryGetSquaredMagnitudeRaw(x.RawValue, y.RawValue, z.RawValue, out long raw)
                ? FPInt64.FromRaw(raw)
                : MaxValue;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static FPInt64 GetSaturatedSquaredMagnitude(FPInt64 x, FPInt64 y, FPInt64 z, FPInt64 w)
        {
            return TryGetSquaredMagnitudeRaw(x.RawValue, y.RawValue, z.RawValue, w.RawValue, out long raw)
                ? FPInt64.FromRaw(raw)
                : MaxValue;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static FPInt64 GetSaturatedSquaredDistance(
            FPInt64 ax,
            FPInt64 ay,
            FPInt64 bx,
            FPInt64 by)
        {
            ulong sum = 0;
            return TryAccumulateSquare(AbsDifferenceRaw(ax.RawValue, bx.RawValue), ref sum) &&
                   TryAccumulateSquare(AbsDifferenceRaw(ay.RawValue, by.RawValue), ref sum)
                ? FPInt64.FromRaw((long)sum)
                : MaxValue;
        }

        internal static bool IsDistanceWithin(in FPVector2 a, in FPVector2 b, ulong maximumDistanceRaw)
        {
            ulong sumHigh = 0;
            ulong sumLow = 0;
            if (!TryAccumulateFullSquare(AbsDifferenceRaw(a.X.RawValue, b.X.RawValue), ref sumHigh, ref sumLow) ||
                !TryAccumulateFullSquare(AbsDifferenceRaw(a.Y.RawValue, b.Y.RawValue), ref sumHigh, ref sumLow))
            {
                return false;
            }

            MultiplyUnsigned64(maximumDistanceRaw, maximumDistanceRaw, out ulong limitHigh, out ulong limitLow);
            return sumHigh < limitHigh || (sumHigh == limitHigh && sumLow <= limitLow);
        }

        internal static bool IsDistanceWithin(in FPVector3 a, in FPVector3 b, ulong maximumDistanceRaw)
        {
            ulong sumHigh = 0;
            ulong sumLow = 0;
            if (!TryAccumulateFullSquare(AbsDifferenceRaw(a.X.RawValue, b.X.RawValue), ref sumHigh, ref sumLow) ||
                !TryAccumulateFullSquare(AbsDifferenceRaw(a.Y.RawValue, b.Y.RawValue), ref sumHigh, ref sumLow) ||
                !TryAccumulateFullSquare(AbsDifferenceRaw(a.Z.RawValue, b.Z.RawValue), ref sumHigh, ref sumLow))
            {
                return false;
            }

            MultiplyUnsigned64(maximumDistanceRaw, maximumDistanceRaw, out ulong limitHigh, out ulong limitLow);
            return sumHigh < limitHigh || (sumHigh == limitHigh && sumLow <= limitLow);
        }

        internal static bool TryDot(
            FPInt64 ax,
            FPInt64 ay,
            FPInt64 bx,
            FPInt64 by,
            out FPInt64 result)
        {
            if (!FPInt64.TryMultiply(ax, bx, out FPInt64 x) ||
                !FPInt64.TryMultiply(ay, by, out FPInt64 y))
            {
                result = default;
                return false;
            }

            return FPInt64.TryAdd(x, y, out result);
        }

        internal static bool TryDot(
            FPInt64 ax,
            FPInt64 ay,
            FPInt64 az,
            FPInt64 bx,
            FPInt64 by,
            FPInt64 bz,
            out FPInt64 result)
        {
            if (!FPInt64.TryMultiply(ax, bx, out FPInt64 x) ||
                !FPInt64.TryMultiply(ay, by, out FPInt64 y) ||
                !FPInt64.TryMultiply(az, bz, out FPInt64 z) ||
                !FPInt64.TryAdd(x, y, out FPInt64 xy))
            {
                result = default;
                return false;
            }

            return FPInt64.TryAdd(xy, z, out result);
        }

        internal static bool TryDot(
            FPInt64 ax,
            FPInt64 ay,
            FPInt64 az,
            FPInt64 aw,
            FPInt64 bx,
            FPInt64 by,
            FPInt64 bz,
            FPInt64 bw,
            out FPInt64 result)
        {
            if (!FPInt64.TryMultiply(ax, bx, out FPInt64 x) ||
                !FPInt64.TryMultiply(ay, by, out FPInt64 y) ||
                !FPInt64.TryMultiply(az, bz, out FPInt64 z) ||
                !FPInt64.TryMultiply(aw, bw, out FPInt64 w) ||
                !FPInt64.TryAdd(x, y, out FPInt64 xy) ||
                !FPInt64.TryAdd(z, w, out FPInt64 zw))
            {
                result = default;
                return false;
            }

            return FPInt64.TryAdd(xy, zw, out result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool TryDot(in FPVector3 a, in FPVector3 b, out FPInt64 result)
        {
            return TryDot(a.X, a.Y, a.Z, b.X, b.Y, b.Z, out result);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static FPInt64 GetSaturatedSquaredDistance(
            FPInt64 ax,
            FPInt64 ay,
            FPInt64 az,
            FPInt64 bx,
            FPInt64 by,
            FPInt64 bz)
        {
            ulong sum = 0;
            return TryAccumulateSquare(AbsDifferenceRaw(ax.RawValue, bx.RawValue), ref sum) &&
                   TryAccumulateSquare(AbsDifferenceRaw(ay.RawValue, by.RawValue), ref sum) &&
                   TryAccumulateSquare(AbsDifferenceRaw(az.RawValue, bz.RawValue), ref sum)
                ? FPInt64.FromRaw((long)sum)
                : MaxValue;
        }

        internal static FPInt64 GetMagnitude(FPInt64 x, FPInt64 y)
        {
            if (TryGetSquaredMagnitudeRaw(x.RawValue, y.RawValue, out long squaredRaw) && squaredRaw != 0)
            {
                return FPInt64.Sqrt(FPInt64.FromRaw(squaredRaw));
            }

            GetScaledComponents(x.RawValue, y.RawValue, out ulong scale, out FPInt64 scaledX, out FPInt64 scaledY);
            FPInt64 scaledMagnitude = FPInt64.Sqrt(scaledX * scaledX + scaledY * scaledY);
            return ScaleMagnitude(scale, scaledMagnitude);
        }

        internal static FPInt64 GetMagnitude(FPInt64 x, FPInt64 y, FPInt64 z)
        {
            if (TryGetSquaredMagnitudeRaw(x.RawValue, y.RawValue, z.RawValue, out long squaredRaw) && squaredRaw != 0)
            {
                return FPInt64.Sqrt(FPInt64.FromRaw(squaredRaw));
            }

            GetScaledComponents(
                x.RawValue,
                y.RawValue,
                z.RawValue,
                out ulong scale,
                out FPInt64 scaledX,
                out FPInt64 scaledY,
                out FPInt64 scaledZ);
            FPInt64 scaledMagnitude = FPInt64.Sqrt(
                scaledX * scaledX + scaledY * scaledY + scaledZ * scaledZ);
            return ScaleMagnitude(scale, scaledMagnitude);
        }

        internal static FPInt64 GetMagnitude(FPInt64 x, FPInt64 y, FPInt64 z, FPInt64 w)
        {
            if (TryGetSquaredMagnitudeRaw(x.RawValue, y.RawValue, z.RawValue, w.RawValue, out long squaredRaw) &&
                squaredRaw != 0)
            {
                return FPInt64.Sqrt(FPInt64.FromRaw(squaredRaw));
            }

            GetScaledComponents(
                x.RawValue,
                y.RawValue,
                z.RawValue,
                w.RawValue,
                out ulong scale,
                out FPInt64 scaledX,
                out FPInt64 scaledY,
                out FPInt64 scaledZ,
                out FPInt64 scaledW);
            FPInt64 scaledMagnitude = FPInt64.Sqrt(
                scaledX * scaledX + scaledY * scaledY + scaledZ * scaledZ + scaledW * scaledW);
            return ScaleMagnitude(scale, scaledMagnitude);
        }

        internal static bool Normalize(FPInt64 x, FPInt64 y, out FPInt64 normalizedX, out FPInt64 normalizedY)
        {
            if (TryGetSquaredMagnitudeRaw(x.RawValue, y.RawValue, out long squaredRaw) && squaredRaw != 0)
            {
                FPInt64 magnitude = FPInt64.Sqrt(FPInt64.FromRaw(squaredRaw));
                if (magnitude.RawValue == 0)
                {
                    normalizedX = default;
                    normalizedY = default;
                    return false;
                }

                normalizedX = x / magnitude;
                normalizedY = y / magnitude;
                return true;
            }

            GetScaledComponents(x.RawValue, y.RawValue, out _, out FPInt64 scaledX, out FPInt64 scaledY);
            FPInt64 scaledMagnitude = FPInt64.Sqrt(scaledX * scaledX + scaledY * scaledY);
            if (scaledMagnitude.RawValue == 0)
            {
                normalizedX = default;
                normalizedY = default;
                return false;
            }

            normalizedX = scaledX / scaledMagnitude;
            normalizedY = scaledY / scaledMagnitude;
            return true;
        }

        internal static bool Normalize(
            FPInt64 x,
            FPInt64 y,
            FPInt64 z,
            out FPInt64 normalizedX,
            out FPInt64 normalizedY,
            out FPInt64 normalizedZ)
        {
            if (TryGetSquaredMagnitudeRaw(x.RawValue, y.RawValue, z.RawValue, out long squaredRaw) && squaredRaw != 0)
            {
                FPInt64 magnitude = FPInt64.Sqrt(FPInt64.FromRaw(squaredRaw));
                if (magnitude.RawValue == 0)
                {
                    normalizedX = default;
                    normalizedY = default;
                    normalizedZ = default;
                    return false;
                }

                normalizedX = x / magnitude;
                normalizedY = y / magnitude;
                normalizedZ = z / magnitude;
                return true;
            }

            GetScaledComponents(
                x.RawValue,
                y.RawValue,
                z.RawValue,
                out _,
                out FPInt64 scaledX,
                out FPInt64 scaledY,
                out FPInt64 scaledZ);
            FPInt64 scaledMagnitude = FPInt64.Sqrt(
                scaledX * scaledX + scaledY * scaledY + scaledZ * scaledZ);
            if (scaledMagnitude.RawValue == 0)
            {
                normalizedX = default;
                normalizedY = default;
                normalizedZ = default;
                return false;
            }

            normalizedX = scaledX / scaledMagnitude;
            normalizedY = scaledY / scaledMagnitude;
            normalizedZ = scaledZ / scaledMagnitude;
            return true;
        }

        internal static bool Normalize(
            FPInt64 x,
            FPInt64 y,
            FPInt64 z,
            FPInt64 w,
            out FPInt64 normalizedX,
            out FPInt64 normalizedY,
            out FPInt64 normalizedZ,
            out FPInt64 normalizedW)
        {
            if (TryGetSquaredMagnitudeRaw(x.RawValue, y.RawValue, z.RawValue, w.RawValue, out long squaredRaw) &&
                squaredRaw != 0)
            {
                FPInt64 magnitude = FPInt64.Sqrt(FPInt64.FromRaw(squaredRaw));
                if (magnitude.RawValue == 0)
                {
                    normalizedX = default;
                    normalizedY = default;
                    normalizedZ = default;
                    normalizedW = default;
                    return false;
                }

                normalizedX = x / magnitude;
                normalizedY = y / magnitude;
                normalizedZ = z / magnitude;
                normalizedW = w / magnitude;
                return true;
            }

            GetScaledComponents(
                x.RawValue,
                y.RawValue,
                z.RawValue,
                w.RawValue,
                out _,
                out FPInt64 scaledX,
                out FPInt64 scaledY,
                out FPInt64 scaledZ,
                out FPInt64 scaledW);
            FPInt64 scaledMagnitude = FPInt64.Sqrt(
                scaledX * scaledX + scaledY * scaledY + scaledZ * scaledZ + scaledW * scaledW);
            if (scaledMagnitude.RawValue == 0)
            {
                normalizedX = default;
                normalizedY = default;
                normalizedZ = default;
                normalizedW = default;
                return false;
            }

            normalizedX = scaledX / scaledMagnitude;
            normalizedY = scaledY / scaledMagnitude;
            normalizedZ = scaledZ / scaledMagnitude;
            normalizedW = scaledW / scaledMagnitude;
            return true;
        }

        private static bool TryGetSquaredMagnitudeRaw(long x, long y, out long squaredRaw)
        {
            ulong sum = 0;
            if (!TryAccumulateSquare(x, ref sum) || !TryAccumulateSquare(y, ref sum))
            {
                squaredRaw = 0;
                return false;
            }

            squaredRaw = (long)sum;
            return true;
        }

        private static bool TryGetSquaredMagnitudeRaw(long x, long y, long z, out long squaredRaw)
        {
            ulong sum = 0;
            if (!TryAccumulateSquare(x, ref sum) ||
                !TryAccumulateSquare(y, ref sum) ||
                !TryAccumulateSquare(z, ref sum))
            {
                squaredRaw = 0;
                return false;
            }

            squaredRaw = (long)sum;
            return true;
        }

        private static bool TryGetSquaredMagnitudeRaw(long x, long y, long z, long w, out long squaredRaw)
        {
            ulong sum = 0;
            if (!TryAccumulateSquare(x, ref sum) ||
                !TryAccumulateSquare(y, ref sum) ||
                !TryAccumulateSquare(z, ref sum) ||
                !TryAccumulateSquare(w, ref sum))
            {
                squaredRaw = 0;
                return false;
            }

            squaredRaw = (long)sum;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryAccumulateSquare(long raw, ref ulong sum)
        {
            return TryAccumulateSquare(AbsRaw(raw), ref sum);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryAccumulateSquare(ulong magnitude, ref ulong sum)
        {
            MultiplyUnsigned64(magnitude, magnitude, out ulong high, out ulong low);
            if (high > 0x7FFFFFFFUL)
            {
                return false;
            }

            ulong squareRaw = (high << FPInt64.FractionalBits) | (low >> FPInt64.FractionalBits);
            if (squareRaw > (ulong)long.MaxValue - sum)
            {
                return false;
            }

            sum += squareRaw;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryAccumulateFullSquare(
            ulong magnitude,
            ref ulong sumHigh,
            ref ulong sumLow)
        {
            MultiplyUnsigned64(magnitude, magnitude, out ulong squareHigh, out ulong squareLow);

            ulong previousLow = sumLow;
            unchecked
            {
                sumLow += squareLow;
                ulong carry = sumLow < previousLow ? 1UL : 0UL;
                if (sumHigh > ulong.MaxValue - squareHigh)
                {
                    return false;
                }

                sumHigh += squareHigh;
                if (carry != 0)
                {
                    if (sumHigh == ulong.MaxValue)
                    {
                        return false;
                    }

                    sumHigh++;
                }
            }

            return true;
        }

        private static void GetScaledComponents(
            long x,
            long y,
            out ulong scale,
            out FPInt64 scaledX,
            out FPInt64 scaledY)
        {
            scale = Max(AbsRaw(x), AbsRaw(y));
            scaledX = DivideByScale(x, scale);
            scaledY = DivideByScale(y, scale);
        }

        private static void GetScaledComponents(
            long x,
            long y,
            long z,
            out ulong scale,
            out FPInt64 scaledX,
            out FPInt64 scaledY,
            out FPInt64 scaledZ)
        {
            scale = Max(Max(AbsRaw(x), AbsRaw(y)), AbsRaw(z));
            scaledX = DivideByScale(x, scale);
            scaledY = DivideByScale(y, scale);
            scaledZ = DivideByScale(z, scale);
        }

        private static void GetScaledComponents(
            long x,
            long y,
            long z,
            long w,
            out ulong scale,
            out FPInt64 scaledX,
            out FPInt64 scaledY,
            out FPInt64 scaledZ,
            out FPInt64 scaledW)
        {
            scale = Max(Max(AbsRaw(x), AbsRaw(y)), Max(AbsRaw(z), AbsRaw(w)));
            scaledX = DivideByScale(x, scale);
            scaledY = DivideByScale(y, scale);
            scaledZ = DivideByScale(z, scale);
            scaledW = DivideByScale(w, scale);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static FPInt64 DivideByScale(long raw, ulong scale)
        {
            if (scale == 0)
            {
                return default;
            }

            if (scale == 0x8000000000000000UL)
            {
                return FPInt64.FromRaw(raw / (1L << 31));
            }

            return FPInt64.FromRaw(raw) / FPInt64.FromRaw((long)scale);
        }

        private static FPInt64 ScaleMagnitude(ulong scale, FPInt64 scaledMagnitude)
        {
            if (scale == 0 || scaledMagnitude.RawValue <= 0)
            {
                return default;
            }

            MultiplyUnsigned64(scale, (ulong)scaledMagnitude.RawValue, out ulong high, out ulong low);
            if (high > 0x7FFFFFFFUL)
            {
                return MaxValue;
            }

            ulong raw = (high << FPInt64.FractionalBits) | (low >> FPInt64.FractionalBits);
            return raw > (ulong)long.MaxValue ? MaxValue : FPInt64.FromRaw((long)raw);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong AbsRaw(long raw)
        {
            return raw < 0
                ? (ulong)(-(raw + 1)) + 1UL
                : (ulong)raw;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong AbsDifferenceRaw(long a, long b)
        {
            ulong orderedA = unchecked((ulong)a) ^ 0x8000000000000000UL;
            ulong orderedB = unchecked((ulong)b) ^ 0x8000000000000000UL;
            return orderedA >= orderedB ? orderedA - orderedB : orderedB - orderedA;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong Max(ulong a, ulong b) => a >= b ? a : b;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void MultiplyUnsigned64(ulong a, ulong b, out ulong high, out ulong low)
        {
            const ulong MASK_32 = 0xFFFFFFFFUL;

            ulong aLow = a & MASK_32;
            ulong aHigh = a >> 32;
            ulong bLow = b & MASK_32;
            ulong bHigh = b >> 32;

            ulong lowLow = aLow * bLow;
            ulong lowHigh = aLow * bHigh;
            ulong highLow = aHigh * bLow;
            ulong highHigh = aHigh * bHigh;

            ulong middle = (lowLow >> 32) + (lowHigh & MASK_32) + (highLow & MASK_32);
            low = (middle << 32) | (lowLow & MASK_32);
            high = highHigh + (lowHigh >> 32) + (highLow >> 32) + (middle >> 32);
        }
    }
}
