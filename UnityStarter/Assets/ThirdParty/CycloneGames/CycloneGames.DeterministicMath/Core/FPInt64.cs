using System;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace CycloneGames.DeterministicMath
{
    /// <summary>
    /// 64-bit fixed-point number in Q32.32 format.
    /// 32 integer bits (range +/-2.1 billion) + 32 fractional bits (precision ~2.3e-10).
    /// All operations are deterministic - bit-identical across all platforms.
    /// <para>
    /// Essential for lockstep networking (deterministic simulation) and rollback netcode
    /// where every client must produce identical results from the same inputs.
    /// </para>
    /// </summary>
    public readonly struct FPInt64 : IEquatable<FPInt64>, IComparable<FPInt64>
    {
        public const int FractionalBits = 32;
        public const long One = 1L << FractionalBits;
        public const long Half = One >> 1;

        public readonly long RawValue;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FPInt64(long rawValue) => RawValue = rawValue;

        // ---- Conversion ----

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 FromInt(int value) => new FPInt64((long)value << FractionalBits);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 FromFloat(float value) => FromFloatUnsafe(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 FromDouble(double value) => FromDoubleUnsafe(value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 FromFloatUnsafe(float value) => new FPInt64((long)(value * One));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 FromDoubleUnsafe(double value) => new FPInt64((long)(value * One));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 FromRaw(long raw) => new FPInt64(raw);

        public static FPInt64 FromString(string value)
        {
            if (!TryParse(value, out var result))
            {
                throw new FormatException("Invalid Q32.32 fixed-point string.");
            }

            return result;
        }

        public static bool TryParse(string value, out FPInt64 result)
        {
            result = default;
            if (string.IsNullOrEmpty(value)) return false;

            int index = 0;
            int length = value.Length;
            bool negative = false;

            if (value[index] == '+' || value[index] == '-')
            {
                negative = value[index] == '-';
                index++;
                if (index == length) return false;
            }

            ulong whole = 0;
            ulong fractionNumerator = 0;
            ulong fractionScale = 1;
            int fractionDigits = 0;
            bool hasDigit = false;
            bool hasDecimalPoint = false;

            for (; index < length; index++)
            {
                char c = value[index];
                if (c == '.')
                {
                    if (hasDecimalPoint) return false;
                    hasDecimalPoint = true;
                    continue;
                }

                if (c < '0' || c > '9') return false;

                hasDigit = true;
                int digit = c - '0';
                if (!hasDecimalPoint)
                {
                    ulong limit = negative ? 2147483648UL : 2147483647UL;
                    if (whole > limit / 10UL) return false;
                    whole = whole * 10UL + (ulong)digit;
                    if (whole > limit) return false;
                }
                else if (fractionDigits < 9)
                {
                    fractionNumerator = fractionNumerator * 10UL + (ulong)digit;
                    fractionScale *= 10UL;
                    fractionDigits++;
                }
            }

            if (!hasDigit) return false;

            long fraction = fractionDigits == 0
                ? 0
                : (long)(fractionNumerator * (ulong)One / fractionScale);
            if (negative && whole == 2147483648UL && fraction != 0) return false;

            ulong rawMagnitude = (whole << FractionalBits) + (ulong)fraction;
            long raw = negative
                ? (rawMagnitude == 0x8000000000000000UL ? long.MinValue : -(long)rawMagnitude)
                : (long)rawMagnitude;
            result = new FPInt64(raw);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ToInt() => (int)(RawValue >> FractionalBits);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float ToFloat() => (float)RawValue / One;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double ToDouble() => (double)RawValue / One;

        // ---- Arithmetic ----

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 operator +(FPInt64 a, FPInt64 b) => new FPInt64(a.RawValue + b.RawValue);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 operator -(FPInt64 a, FPInt64 b) => new FPInt64(a.RawValue - b.RawValue);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 operator -(FPInt64 a) => new FPInt64(-a.RawValue);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 operator *(FPInt64 a, FPInt64 b)
        {
            long aRaw = a.RawValue;
            long bRaw = b.RawValue;

            bool negative = (aRaw ^ bRaw) < 0;
            ulong ua = (ulong)(aRaw < 0 ? -aRaw : aRaw);
            ulong ub = (ulong)(bRaw < 0 ? -bRaw : bRaw);

            ulong aHi = ua >> 32;
            ulong aLo = ua & 0xFFFFFFFFUL;
            ulong bHi = ub >> 32;
            ulong bLo = ub & 0xFFFFFFFFUL;

            ulong loLo = aLo * bLo;
            ulong loHi = aLo * bHi;
            ulong hiLo = aHi * bLo;
            ulong hiHi = aHi * bHi;

            ulong r = (loLo >> 32) + (loHi & 0xFFFFFFFFUL) + (hiLo & 0xFFFFFFFFUL);
            ulong finalResult = hiHi + (loHi >> 32) + (hiLo >> 32) + (r >> 32);
            finalResult = (finalResult << 32) | (r & 0xFFFFFFFFUL);

            return new FPInt64(negative ? -(long)finalResult : (long)finalResult);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 operator /(FPInt64 a, FPInt64 b)
        {
            if (b.RawValue == 0) throw new DivideByZeroException();

            // Q32.32 divisors that encode powers of two can use raw shifts directly.
            long bv = b.RawValue;
            long bvAbs = bv < 0 ? -bv : bv;
            if (bvAbs > 0 && (bvAbs & (bvAbs - 1)) == 0)
            {
                int shift = BitScanForward(bvAbs) - FractionalBits;
                long raw = shift >= 0
                    ? a.RawValue >> shift
                    : a.RawValue << -shift;
                return new FPInt64(bv < 0 ? -raw : raw);
            }

            bool negative = (a.RawValue ^ b.RawValue) < 0;
            ulong ua = (ulong)(a.RawValue < 0 ? -a.RawValue : a.RawValue);
            ulong ub = (ulong)(b.RawValue < 0 ? -b.RawValue : b.RawValue);

            ulong numHi = ua >> 32;
            ulong numLo = ua << 32;

            ulong quotient;
            if (numHi == 0)
            {
                quotient = numLo / ub;
            }
            else
            {
                ulong qHi = numHi / ub;
                ulong rem = numHi % ub;
                ulong qLo = 0;
                for (int i = 63; i >= 0; i--)
                {
                    rem <<= 1;
                    rem |= (numLo >> i) & 1UL;
                    if (rem >= ub)
                    {
                        rem -= ub;
                        qLo |= 1UL << i;
                    }
                }
                quotient = (qHi << 32) + qLo;
            }

            return new FPInt64(negative ? -(long)quotient : (long)quotient);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 operator %(FPInt64 a, FPInt64 b) => new FPInt64(a.RawValue % b.RawValue);

        // ---- Comparison ----

        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool operator ==(FPInt64 a, FPInt64 b) => a.RawValue == b.RawValue;
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool operator !=(FPInt64 a, FPInt64 b) => a.RawValue != b.RawValue;
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool operator <(FPInt64 a, FPInt64 b) => a.RawValue < b.RawValue;
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool operator >(FPInt64 a, FPInt64 b) => a.RawValue > b.RawValue;
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool operator <=(FPInt64 a, FPInt64 b) => a.RawValue <= b.RawValue;
        [MethodImpl(MethodImplOptions.AggressiveInlining)] public static bool operator >=(FPInt64 a, FPInt64 b) => a.RawValue >= b.RawValue;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator FPInt64(int value) => FromInt(value);

        // ---- Math ----

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 Abs(FPInt64 v) => new FPInt64(v.RawValue >= 0 ? v.RawValue : -v.RawValue);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 Min(FPInt64 a, FPInt64 b) => a.RawValue <= b.RawValue ? a : b;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 Max(FPInt64 a, FPInt64 b) => a.RawValue >= b.RawValue ? a : b;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 Clamp(FPInt64 v, FPInt64 min, FPInt64 max)
        {
            if (v.RawValue < min.RawValue) return min;
            if (v.RawValue > max.RawValue) return max;
            return v;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 Lerp(FPInt64 a, FPInt64 b, FPInt64 t) => a + (b - a) * t;

        /// <summary>Round down to the nearest integer. 1.7 -> 1, -1.7 -> -2.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 Floor(FPInt64 v)
        {
            long raw = v.RawValue;
            long truncated = (raw / One) * One;
            if (raw < 0 && raw % One != 0)
            {
                truncated -= One;
            }
            return new FPInt64(truncated);
        }

        /// <summary>Round up to the nearest integer. 1.3 -> 2, -1.3 -> -1.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 Ceil(FPInt64 v)
        {
            long raw = v.RawValue;
            long truncated = (raw / One) * One;
            if (raw > 0 && raw % One != 0)
            {
                truncated += One;
            }
            return new FPInt64(truncated);
        }

        /// <summary>Round to the nearest integer (midpoint rounds away from zero). 1.5 -> 2, -1.5 -> -2.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 Round(FPInt64 v)
        {
            long raw = v.RawValue;
            if (raw >= 0)
            {
                return new FPInt64((raw + Half) & ~(One - 1));
            }

            return -Floor(Abs(v) + FromRaw(Half));
        }

        /// <summary>
        /// Integer square root in Q32.32 format. Deterministic and overflow-safe.
        /// </summary>
        public static FPInt64 Sqrt(FPInt64 v)
        {
            if (v.RawValue <= 0) return default;

            ulong targetRaw = (ulong)v.RawValue;
            ulong low = 1;
            ulong high = 1UL << 48;
            ulong result = 0;

            while (low <= high)
            {
                ulong mid = (low + high) >> 1;
                if (SquareLessOrEqualShifted(mid, targetRaw))
                {
                    result = mid;
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }

            return new FPInt64((long)result);
        }

        // ---- Constants ----

        public static readonly FPInt64 Zero = default;
        public static readonly FPInt64 OneValue = FromInt(1);
        public static readonly FPInt64 MinusOne = FromInt(-1);
        public static readonly FPInt64 Pi = FromRaw(13493037705L);
        public static readonly FPInt64 TwoPi = FromRaw(26986075409L);
        public static readonly FPInt64 HalfPi = FromRaw(6746518852L);
        public static readonly FPInt64 Deg2Rad = FromRaw(74961321L);
        public static readonly FPInt64 Rad2Deg = FromRaw(246083499208L);

        // ---- Internal ----

        /// <summary>Returns the index of the least significant set bit (0-63). Only called for non-zero power-of-2 values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int BitScanForward(long v)
        {
            int index = 0;
            ulong x = (ulong)v;
            while ((x & 1UL) == 0)
            {
                x >>= 1;
                index++;
            }

            return index;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool SquareLessOrEqualShifted(ulong value, ulong shiftedSourceRaw)
        {
            MultiplyUnsigned64(value, value, out ulong squareHigh, out ulong squareLow);
            ulong targetHigh = shiftedSourceRaw >> 32;
            ulong targetLow = shiftedSourceRaw << 32;

            return squareHigh < targetHigh ||
                   (squareHigh == targetHigh && squareLow <= targetLow);
        }

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

        // ---- IEquatable / IComparable ----

        public bool Equals(FPInt64 other) => RawValue == other.RawValue;
        public override bool Equals(object obj) => obj is FPInt64 fp && fp.RawValue == RawValue;
        public override int GetHashCode() => RawValue.GetHashCode();
        public int CompareTo(FPInt64 other) => RawValue.CompareTo(other.RawValue);
        public override string ToString() => ToDouble().ToString("F6", CultureInfo.InvariantCulture);
    }
}
