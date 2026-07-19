using System;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace CycloneGames.DeterministicMath
{
    /// <summary>
    /// 64-bit fixed-point number in Q32.32 format.
    /// 32 integer bits (range +/-2.1 billion) + 32 fractional bits (precision ~2.3e-10).
    /// <para>
    /// Operators intentionally use unchecked two's-complement wrapping. Use the additive Try methods when input
    /// ranges are not already proven safe by the simulation contract.
    /// </para>
    /// </summary>
    public readonly struct FPInt64 : IEquatable<FPInt64>, IComparable<FPInt64>
    {
        public const int FractionalBits = 32;
        public const long RAW_ONE = 1L << FractionalBits;
        public const long RAW_HALF = RAW_ONE >> 1;

        private const double MIN_INTEGER_VALUE = -2147483648d;
        private const double MAX_EXCLUSIVE_VALUE = 2147483648d;
        private const ulong SIGN_BIT = 0x8000000000000000UL;

        public readonly long RawValue;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private FPInt64(long rawValue) => RawValue = rawValue;

        // ---- Conversion ----

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 FromInt(int value) => new FPInt64((long)value << FractionalBits);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 FromFloat(float value)
        {
            if (!TryFromFloat(value, out FPInt64 result))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Value must be finite and within the Q32.32 range.");
            }

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 FromDouble(double value)
        {
            if (!TryFromDouble(value, out FPInt64 result))
            {
                throw new ArgumentOutOfRangeException(nameof(value), "Value must be finite and within the Q32.32 range.");
            }

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryFromFloat(float value, out FPInt64 result)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) ||
                value < MIN_INTEGER_VALUE || value >= MAX_EXCLUSIVE_VALUE)
            {
                result = default;
                return false;
            }

            result = new FPInt64((long)(value * RAW_ONE));
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryFromDouble(double value, out FPInt64 result)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) ||
                value < MIN_INTEGER_VALUE || value >= MAX_EXCLUSIVE_VALUE)
            {
                result = default;
                return false;
            }

            result = new FPInt64((long)(value * RAW_ONE));
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 FromRaw(long raw) => new FPInt64(raw);

        public static FPInt64 Parse(string value)
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
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            int index = 0;
            bool negative = false;
            if (value[index] == '+' || value[index] == '-')
            {
                negative = value[index] == '-';
                index++;
                if (index == value.Length)
                {
                    return false;
                }
            }

            ulong whole = 0;
            ulong fractionHigh = 0;
            ulong fractionLow = 0;
            ulong scaleHigh = 0;
            ulong scaleLow = 1;
            int fractionDigits = 0;
            bool hasDigit = false;
            bool hasDecimalPoint = false;

            for (; index < value.Length; index++)
            {
                char c = value[index];
                if (c == '.')
                {
                    if (hasDecimalPoint)
                    {
                        return false;
                    }

                    hasDecimalPoint = true;
                    continue;
                }

                if (c < '0' || c > '9')
                {
                    return false;
                }

                hasDigit = true;
                uint digit = (uint)(c - '0');
                if (!hasDecimalPoint)
                {
                    ulong limit = negative ? 2147483648UL : 2147483647UL;
                    if (whole > limit / 10UL)
                    {
                        return false;
                    }

                    whole = whole * 10UL + digit;
                    if (whole > limit)
                    {
                        return false;
                    }
                }
                else if (fractionDigits < FractionalBits)
                {
                    Multiply128By10AndAdd(ref fractionHigh, ref fractionLow, digit);
                    Multiply128By10AndAdd(ref scaleHigh, ref scaleLow, 0);
                    fractionDigits++;
                }
                else if (digit != 0)
                {
                    // Q32.32 values have an exact decimal expansion of at most 32 fractional digits.
                    return false;
                }
            }

            if (!hasDigit)
            {
                return false;
            }

            ulong fractionRaw = fractionDigits == 0
                ? 0
                : DivideFractionToRaw(fractionHigh, fractionLow, scaleHigh, scaleLow);

            if (fractionRaw >= (ulong)RAW_ONE)
            {
                whole++;
                fractionRaw -= (ulong)RAW_ONE;
            }

            ulong rawMagnitude = (whole << FractionalBits) + fractionRaw;
            ulong maxMagnitude = negative ? SIGN_BIT : (ulong)long.MaxValue;
            if (rawMagnitude > maxMagnitude)
            {
                return false;
            }

            result = new FPInt64(ApplySign(rawMagnitude, negative));
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ToInt() => (int)(RawValue / RAW_ONE);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float ToFloat() => (float)RawValue / RAW_ONE;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double ToDouble() => (double)RawValue / RAW_ONE;

        // ---- Arithmetic ----

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 operator +(FPInt64 a, FPInt64 b) =>
            new FPInt64(unchecked(a.RawValue + b.RawValue));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 operator -(FPInt64 a, FPInt64 b) =>
            new FPInt64(unchecked(a.RawValue - b.RawValue));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 operator -(FPInt64 a) => new FPInt64(unchecked(-a.RawValue));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 operator *(FPInt64 a, FPInt64 b)
        {
            bool negative = (a.RawValue ^ b.RawValue) < 0;
            MultiplyUnsigned64(
                UnsignedMagnitude(a.RawValue),
                UnsignedMagnitude(b.RawValue),
                out ulong productHigh,
                out ulong productLow);
            ulong rawMagnitude = (productHigh << FractionalBits) | (productLow >> FractionalBits);
            return new FPInt64(ApplySign(rawMagnitude, negative));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 operator /(FPInt64 a, FPInt64 b)
        {
            if (b.RawValue == 0)
            {
                throw new DivideByZeroException();
            }

            return new FPInt64(DivideRawWrapping(a.RawValue, b.RawValue));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 operator %(FPInt64 a, FPInt64 b)
        {
            if (b.RawValue == 0)
            {
                throw new DivideByZeroException();
            }

            if (a.RawValue == long.MinValue && b.RawValue == -1)
            {
                return Zero;
            }

            return new FPInt64(a.RawValue % b.RawValue);
        }

        /// <summary>Attempts addition without allowing the raw signed result to overflow.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryAdd(FPInt64 a, FPInt64 b, out FPInt64 result)
        {
            long raw = unchecked(a.RawValue + b.RawValue);
            if (((a.RawValue ^ raw) & (b.RawValue ^ raw)) < 0)
            {
                result = default;
                return false;
            }

            result = new FPInt64(raw);
            return true;
        }

        /// <summary>Attempts subtraction without allowing the raw signed result to overflow.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TrySubtract(FPInt64 a, FPInt64 b, out FPInt64 result)
        {
            long raw = unchecked(a.RawValue - b.RawValue);
            if (((a.RawValue ^ b.RawValue) & (a.RawValue ^ raw)) < 0)
            {
                result = default;
                return false;
            }

            result = new FPInt64(raw);
            return true;
        }

        /// <summary>Attempts negation without allowing <see cref="MinValue"/> to wrap.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryNegate(FPInt64 value, out FPInt64 result)
        {
            if (value.RawValue == long.MinValue)
            {
                result = default;
                return false;
            }

            result = new FPInt64(-value.RawValue);
            return true;
        }

        /// <summary>
        /// Attempts Q32.32 multiplication with truncation toward zero and without allowing the raw result to overflow.
        /// </summary>
        public static bool TryMultiply(FPInt64 a, FPInt64 b, out FPInt64 result)
        {
            bool negative = (a.RawValue ^ b.RawValue) < 0;
            MultiplyUnsigned64(
                UnsignedMagnitude(a.RawValue),
                UnsignedMagnitude(b.RawValue),
                out ulong productHigh,
                out ulong productLow);

            ulong quotientHigh = productHigh >> FractionalBits;
            ulong quotientLow = (productHigh << FractionalBits) | (productLow >> FractionalBits);
            ulong maxMagnitude = negative ? SIGN_BIT : (ulong)long.MaxValue;
            if (quotientHigh != 0 || quotientLow > maxMagnitude)
            {
                result = default;
                return false;
            }

            result = new FPInt64(ApplySign(quotientLow, negative));
            return true;
        }

        /// <summary>
        /// Attempts the fused fixed-point expression <c>(a * b) / divisor</c> with a full 128-bit intermediate.
        /// </summary>
        public static bool TryMultiplyDivide(
            FPInt64 a,
            FPInt64 b,
            FPInt64 divisor,
            out FPInt64 result)
        {
            result = default;
            if (divisor.RawValue == 0)
            {
                return false;
            }

            bool negative = (a.RawValue ^ b.RawValue ^ divisor.RawValue) < 0;
            MultiplyUnsigned64(
                UnsignedMagnitude(a.RawValue),
                UnsignedMagnitude(b.RawValue),
                out ulong numeratorHigh,
                out ulong numeratorLow);
            DivideUnsigned128(
                numeratorHigh,
                numeratorLow,
                UnsignedMagnitude(divisor.RawValue),
                out ulong quotientHigh,
                out ulong quotientLow);

            ulong maxMagnitude = negative ? SIGN_BIT : (ulong)long.MaxValue;
            if (quotientHigh != 0 || quotientLow > maxMagnitude)
            {
                return false;
            }

            result = new FPInt64(ApplySign(quotientLow, negative));
            return true;
        }

        /// <summary>
        /// Attempts Q32.32 division with truncation toward zero.
        /// Returns false for a zero divisor or when the mathematical quotient is outside the Q32.32 raw range.
        /// </summary>
        public static bool TryDivide(FPInt64 a, FPInt64 b, out FPInt64 result)
        {
            result = default;
            if (b.RawValue == 0)
            {
                return false;
            }

            bool negative = (a.RawValue ^ b.RawValue) < 0;
            ulong dividend = UnsignedMagnitude(a.RawValue);
            ulong divisor = UnsignedMagnitude(b.RawValue);
            DivideUnsignedShifted(dividend, divisor, out ulong quotientHigh, out ulong quotientLow);

            ulong maxMagnitude = negative ? SIGN_BIT : (ulong)long.MaxValue;
            if (quotientHigh != 0 || quotientLow > maxMagnitude)
            {
                return false;
            }

            result = new FPInt64(ApplySign(quotientLow, negative));
            return true;
        }

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

        /// <summary>Returns the absolute value.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 Abs(FPInt64 v)
        {
            if (!TryAbs(v, out FPInt64 result))
            {
                throw new OverflowException("The absolute value of FPInt64.MinValue is not representable.");
            }

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryAbs(FPInt64 v, out FPInt64 result)
        {
            if (v.RawValue == long.MinValue)
            {
                result = default;
                return false;
            }

            result = new FPInt64(v.RawValue >= 0 ? v.RawValue : -v.RawValue);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 Min(FPInt64 a, FPInt64 b) => a.RawValue <= b.RawValue ? a : b;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 Max(FPInt64 a, FPInt64 b) => a.RawValue >= b.RawValue ? a : b;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 Clamp(FPInt64 v, FPInt64 min, FPInt64 max)
        {
            if (v.RawValue < min.RawValue)
            {
                return min;
            }

            if (v.RawValue > max.RawValue)
            {
                return max;
            }

            return v;
        }

        /// <summary>Linearly interpolates between two values with <paramref name="t"/> clamped to [0, 1].</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 Lerp(FPInt64 a, FPInt64 b, FPInt64 t) =>
            LerpUnclamped(a, b, Clamp(t, Zero, One));

        /// <summary>Linearly interpolates or extrapolates without clamping <paramref name="t"/>.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 LerpUnclamped(FPInt64 a, FPInt64 b, FPInt64 t) =>
            a + (b - a) * t;

        /// <summary>Round down to the nearest integer. 1.7 -> 1, -1.7 -> -2.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 Floor(FPInt64 v)
        {
            long raw = v.RawValue;
            long truncated = (raw / RAW_ONE) * RAW_ONE;
            if (raw < 0 && raw % RAW_ONE != 0)
            {
                truncated -= RAW_ONE;
            }
            return new FPInt64(truncated);
        }

        /// <summary>Round up to the nearest integer. 1.3 -> 2, -1.3 -> -1.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 Ceil(FPInt64 v)
        {
            if (TryCeil(v, out FPInt64 result))
            {
                return result;
            }

            throw new OverflowException("The rounded integer is outside the Q32.32 range.");
        }

        /// <summary>Attempts to round up without overflowing the Q32.32 range.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryCeil(FPInt64 v, out FPInt64 result)
        {
            long raw = v.RawValue;
            long truncated = (raw / RAW_ONE) * RAW_ONE;
            if (raw > 0 && raw % RAW_ONE != 0)
            {
                if (truncated > long.MaxValue - RAW_ONE)
                {
                    result = default;
                    return false;
                }

                truncated += RAW_ONE;
            }

            result = new FPInt64(truncated);
            return true;
        }

        /// <summary>Round to the nearest integer (midpoint rounds away from zero). 1.5 -> 2, -1.5 -> -2.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 Round(FPInt64 v)
        {
            if (TryRound(v, out FPInt64 result))
            {
                return result;
            }

            throw new OverflowException("The rounded integer is outside the Q32.32 range.");
        }

        /// <summary>Attempts midpoint-away-from-zero rounding without overflowing the Q32.32 range.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryRound(FPInt64 v, out FPInt64 result)
        {
            long raw = v.RawValue;
            long truncated = (raw / RAW_ONE) * RAW_ONE;
            long remainder = raw % RAW_ONE;

            if (raw >= 0)
            {
                if (remainder >= RAW_HALF)
                {
                    if (truncated > long.MaxValue - RAW_ONE)
                    {
                        result = default;
                        return false;
                    }

                    truncated += RAW_ONE;
                }
            }
            else if (remainder <= -RAW_HALF)
            {
                truncated -= RAW_ONE;
            }

            result = new FPInt64(truncated);
            return true;
        }

        /// <summary>
        /// Integer square root in Q32.32 format. Deterministic and overflow-safe.
        /// </summary>
        public static FPInt64 Sqrt(FPInt64 v)
        {
            if (!TrySqrt(v, out FPInt64 result))
            {
                throw new ArgumentOutOfRangeException(nameof(v), "Square root is undefined for negative values.");
            }

            return result;
        }

        /// <summary>Attempts to calculate the floor square root. Returns false for a negative input.</summary>
        public static bool TrySqrt(FPInt64 v, out FPInt64 result)
        {
            if (v.RawValue < 0)
            {
                result = default;
                return false;
            }

            if (v.RawValue == 0)
            {
                result = default;
                return true;
            }

            ulong targetRaw = (ulong)v.RawValue;
            ulong low = 1;
            ulong high = 1UL << 48;
            ulong root = 0;

            while (low <= high)
            {
                ulong mid = (low + high) >> 1;
                if (SquareLessOrEqualShifted(mid, targetRaw))
                {
                    root = mid;
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }

            result = new FPInt64((long)root);
            return true;
        }

        // ---- Constants ----

        public static readonly FPInt64 Zero = default;
        public static readonly FPInt64 One = FromRaw(RAW_ONE);
        public static readonly FPInt64 Half = FromRaw(RAW_HALF);
        public static readonly FPInt64 MinusOne = FromInt(-1);
        public static readonly FPInt64 MinValue = FromRaw(long.MinValue);
        public static readonly FPInt64 MaxValue = FromRaw(long.MaxValue);
        public static readonly FPInt64 Pi = FromRaw(13493037705L);
        public static readonly FPInt64 TwoPi = FromRaw(26986075409L);
        public static readonly FPInt64 HalfPi = FromRaw(6746518852L);
        public static readonly FPInt64 Deg2Rad = FromRaw(74961321L);
        public static readonly FPInt64 Rad2Deg = FromRaw(246083499208L);

        // ---- Internal ----

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Multiply128By10AndAdd(ref ulong high, ref ulong low, uint digit)
        {
            MultiplyUnsigned64(low, 10UL, out ulong carry, out ulong productLow);
            unchecked
            {
                high = high * 10UL + carry;
                ulong previousLow = productLow;
                productLow += digit;
                if (productLow < previousLow)
                {
                    high++;
                }
            }

            low = productLow;
        }

        private static ulong DivideFractionToRaw(
            ulong numeratorHigh,
            ulong numeratorLow,
            ulong denominatorHigh,
            ulong denominatorLow)
        {
            ulong remainderHigh = numeratorHigh;
            ulong remainderLow = numeratorLow;
            ulong quotient = 0;

            for (int i = 0; i < FractionalBits; i++)
            {
                ShiftLeft128(ref remainderHigh, ref remainderLow);
                quotient <<= 1;
                if (Compare128(remainderHigh, remainderLow, denominatorHigh, denominatorLow) >= 0)
                {
                    Subtract128(
                        ref remainderHigh,
                        ref remainderLow,
                        denominatorHigh,
                        denominatorLow);
                    quotient |= 1UL;
                }
            }

            ShiftLeft128(ref remainderHigh, ref remainderLow);
            if (Compare128(remainderHigh, remainderLow, denominatorHigh, denominatorLow) >= 0)
            {
                quotient++;
            }

            return quotient;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ShiftLeft128(ref ulong high, ref ulong low)
        {
            high = (high << 1) | (low >> 63);
            low <<= 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Compare128(ulong leftHigh, ulong leftLow, ulong rightHigh, ulong rightLow)
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
        private static void Subtract128(
            ref ulong leftHigh,
            ref ulong leftLow,
            ulong rightHigh,
            ulong rightLow)
        {
            ulong previousLow = leftLow;
            unchecked
            {
                leftLow -= rightLow;
                leftHigh = leftHigh - rightHigh - (previousLow < rightLow ? 1UL : 0UL);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long DivideRawWrapping(long dividendRaw, long divisorRaw)
        {
            bool negative = (dividendRaw ^ divisorRaw) < 0;
            ulong dividend = UnsignedMagnitude(dividendRaw);
            ulong divisor = UnsignedMagnitude(divisorRaw);

            if ((divisor & (divisor - 1UL)) == 0)
            {
                int divisorBit = BitScanForward(divisor);
                ulong quotient = divisorBit < FractionalBits
                    ? dividend << (FractionalBits - divisorBit)
                    : dividend >> (divisorBit - FractionalBits);
                return ApplySign(quotient, negative);
            }

            DivideUnsignedShifted(dividend, divisor, out _, out ulong quotientLow);
            return ApplySign(quotientLow, negative);
        }

        private static void DivideUnsignedShifted(
            ulong dividend,
            ulong divisor,
            out ulong quotientHigh,
            out ulong quotientLow)
        {
            ulong numeratorHigh = dividend >> FractionalBits;
            ulong numeratorLow = dividend << FractionalBits;

            DivideUnsigned128(
                numeratorHigh,
                numeratorLow,
                divisor,
                out quotientHigh,
                out quotientLow);
        }

        private static void DivideUnsigned128(
            ulong numeratorHigh,
            ulong numeratorLow,
            ulong divisor,
            out ulong quotientHigh,
            out ulong quotientLow)
        {

            quotientHigh = numeratorHigh / divisor;
            ulong remainder = numeratorHigh % divisor;
            quotientLow = 0;

            for (int i = 63; i >= 0; i--)
            {
                remainder = (remainder << 1) | ((numeratorLow >> i) & 1UL);
                if (remainder >= divisor)
                {
                    remainder -= divisor;
                    quotientLow |= 1UL << i;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong UnsignedMagnitude(long value)
        {
            ulong raw = unchecked((ulong)value);
            return value < 0 ? unchecked(0UL - raw) : raw;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long ApplySign(ulong magnitude, bool negative)
        {
            ulong raw = negative ? unchecked(0UL - magnitude) : magnitude;
            return unchecked((long)raw);
        }

        /// <summary>Returns the index of the least significant set bit (0-63). Only called for non-zero power-of-2 values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int BitScanForward(ulong value)
        {
            int index = 0;
            while ((value & 1UL) == 0)
            {
                value >>= 1;
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

            unchecked
            {
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

        // ---- IEquatable / IComparable ----

        public bool Equals(FPInt64 other) => RawValue == other.RawValue;
        public override bool Equals(object obj) => obj is FPInt64 fp && fp.RawValue == RawValue;
        public override int GetHashCode() => RawValue.GetHashCode();
        public int CompareTo(FPInt64 other) => RawValue.CompareTo(other.RawValue);

        /// <summary>Formats the exact invariant decimal value so parsing the result restores the same raw bits.</summary>
        public override string ToString()
        {
            ulong magnitude = UnsignedMagnitude(RawValue);
            ulong whole = magnitude >> FractionalBits;
            uint fraction = unchecked((uint)magnitude);

            StringBuilder builder = new StringBuilder(43);
            if (RawValue < 0)
            {
                builder.Append('-');
            }

            builder.Append(whole.ToString(CultureInfo.InvariantCulture));
            if (fraction == 0)
            {
                return builder.ToString();
            }

            builder.Append('.');
            for (int i = 0; i < FractionalBits && fraction != 0; i++)
            {
                ulong scaled = (ulong)fraction * 10UL;
                builder.Append((char)('0' + (scaled >> FractionalBits)));
                fraction = unchecked((uint)scaled);
            }

            return builder.ToString();
        }
    }
}
