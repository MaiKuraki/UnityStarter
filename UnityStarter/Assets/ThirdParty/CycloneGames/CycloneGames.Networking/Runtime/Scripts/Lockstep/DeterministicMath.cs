using System;
using System.Runtime.CompilerServices;

namespace CycloneGames.Networking.Lockstep
{
    /// <summary>
    /// 64-bit fixed-point number (Q32.32 format) for deterministic cross-platform math.
    /// All operations produce bit-identical results regardless of CPU/OS/compiler.
    /// Essential for lockstep games (Red Alert, StarCraft, Age of Empires).
    /// 
    /// Range: roughly ±2,147,483,647 with 32-bit fractional precision (~2.3e-10).
    /// </summary>
    public readonly struct FPInt64 : IEquatable<FPInt64>, IComparable<FPInt64>
    {
        public const int FractionalBits = 32;
        public const long One = 1L << FractionalBits;
        public const long Half = One >> 1;

        public readonly long RawValue;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FPInt64(long rawValue) => RawValue = rawValue;

        // Conversion from/to standard types
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 FromInt(int value) => new FPInt64((long)value << FractionalBits);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 FromFloat(float value) => new FPInt64((long)(value * One));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 FromDouble(double value) => new FPInt64((long)(value * One));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 FromRaw(long raw) => new FPInt64(raw);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ToInt() => (int)(RawValue >> FractionalBits);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float ToFloat() => (float)RawValue / One;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double ToDouble() => (double)RawValue / One;

        // Arithmetic operators
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 operator +(FPInt64 a, FPInt64 b) => new FPInt64(a.RawValue + b.RawValue);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 operator -(FPInt64 a, FPInt64 b) => new FPInt64(a.RawValue - b.RawValue);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 operator -(FPInt64 a) => new FPInt64(-a.RawValue);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 operator *(FPInt64 a, FPInt64 b)
        {
            // Split into unsigned 32-bit halves for correct 128-bit intermediate.
            // Result = (a * b) >> 32, computed without overflow.
            long aRaw = a.RawValue;
            long bRaw = b.RawValue;

            // Determine result sign, work with absolute values using unsigned math
            bool negative = (aRaw ^ bRaw) < 0;
            ulong ua = (ulong)(aRaw < 0 ? -aRaw : aRaw);
            ulong ub = (ulong)(bRaw < 0 ? -bRaw : bRaw);

            ulong aHi = ua >> 32;
            ulong aLo = ua & 0xFFFFFFFFUL;
            ulong bHi = ub >> 32;
            ulong bLo = ub & 0xFFFFFFFFUL;

            // Full 128-bit product split into parts:
            // ua * ub = (aHi*bHi)<<64 + (aHi*bLo + aLo*bHi)<<32 + aLo*bLo
            // We need bits [95..32] of the 128-bit product (>> 32).
            ulong loLo = aLo * bLo;
            ulong loHi = aLo * bHi;
            ulong hiLo = aHi * bLo;
            ulong hiHi = aHi * bHi;

            // Accumulate, starting from the lowest contributing part
            ulong mid = (loLo >> 32) + (loHi & 0xFFFFFFFFUL) + (hiLo & 0xFFFFFFFFUL);
            // Now the result's lower 32 bits come from mid's lower 32 bits (carry propagation)
            ulong result = hiHi + (loHi >> 32) + (hiLo >> 32) + (mid >> 32);
            // Combine with the lower 32 bits
            result = (result << 32) | (mid & 0xFFFFFFFFUL);

            // We actually want the full 64-bit result from bits [95..32]:
            // result already has bits [127..32] >> 32 in the upper part.
            // Recalculate cleanly:
            ulong r = (loLo >> 32) + (loHi & 0xFFFFFFFFUL) + (hiLo & 0xFFFFFFFFUL);
            ulong finalResult = hiHi + (loHi >> 32) + (hiLo >> 32) + (r >> 32);
            finalResult = (finalResult << 32) | (r & 0xFFFFFFFFUL);

            return new FPInt64(negative ? -(long)finalResult : (long)finalResult);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 operator /(FPInt64 a, FPInt64 b)
        {
            if (b.RawValue == 0) throw new DivideByZeroException();

            // Compute (a << 32) / b using 128-bit numerator via long division.
            bool negative = (a.RawValue ^ b.RawValue) < 0;
            ulong ua = (ulong)(a.RawValue < 0 ? -a.RawValue : a.RawValue);
            ulong ub = (ulong)(b.RawValue < 0 ? -b.RawValue : b.RawValue);

            // numerator = ua << 32, which is 96 bits max.
            // Split into high and low 64-bit parts: numHi = ua >> 32, numLo = ua << 32
            ulong numHi = ua >> 32;
            ulong numLo = ua << 32;

            // Perform 128/64 division using two 64-bit divisions
            ulong quotient;
            if (numHi == 0)
            {
                quotient = numLo / ub;
            }
            else
            {
                // Long division: divide numHi:numLo by ub
                ulong qHi = numHi / ub;
                ulong rem = numHi % ub;
                // Now divide (rem << 64 | numLo) by ub — but rem < ub, so rem << 64 fits conceptually.
                // We split this: result = qHi << 64 + (rem:numLo) / ub
                // Since qHi << 64 may overflow a single ulong, and in practice for Q32.32
                // the result should fit in 64 bits, we can simplify:
                // For safe 64-bit result, do iterative shift-subtract
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
                quotient = (qHi << 32) + qLo; // qHi contributes to upper bits
            }

            return new FPInt64(negative ? -(long)quotient : (long)quotient);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 operator %(FPInt64 a, FPInt64 b) => new FPInt64(a.RawValue % b.RawValue);

        // Comparison
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(FPInt64 a, FPInt64 b) => a.RawValue == b.RawValue;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(FPInt64 a, FPInt64 b) => a.RawValue != b.RawValue;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <(FPInt64 a, FPInt64 b) => a.RawValue < b.RawValue;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >(FPInt64 a, FPInt64 b) => a.RawValue > b.RawValue;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <=(FPInt64 a, FPInt64 b) => a.RawValue <= b.RawValue;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >=(FPInt64 a, FPInt64 b) => a.RawValue >= b.RawValue;

        // Implicit conversion from int
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator FPInt64(int value) => FromInt(value);

        // Math functions (all deterministic, no floating-point)
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

        /// <summary>
        /// Integer square root via Newton's method (deterministic).
        /// </summary>
        public static FPInt64 Sqrt(FPInt64 v)
        {
            if (v.RawValue <= 0) return default;

            // Initial guess via bit shifting
            long raw = v.RawValue << FractionalBits; // Scale up for precision
            long guess = v.RawValue;

            // Find a reasonable starting point
            int shift = 0;
            long temp = v.RawValue;
            while (temp > 0) { temp >>= 1; shift++; }
            guess = 1L << ((shift + FractionalBits) >> 1);

            // Newton iterations (converges fast for fixed-point)
            for (int i = 0; i < 8; i++)
            {
                if (guess == 0) break;
                long next = (guess + raw / guess) >> 1;
                if (next == guess) break;
                guess = next;
            }

            return new FPInt64(guess);
        }

        // Pre-computed constants
        public static readonly FPInt64 Zero = default;
        public static readonly FPInt64 OneValue = FromInt(1);
        public static readonly FPInt64 MinusOne = FromInt(-1);
        public static readonly FPInt64 Pi = FromRaw(13493037705L);        // π ≈ 3.14159265359
        public static readonly FPInt64 TwoPi = FromRaw(26986075409L);     // 2π
        public static readonly FPInt64 HalfPi = FromRaw(6746518852L);     // π/2
        public static readonly FPInt64 Deg2Rad = FromRaw(74961321L);      // π/180
        public static readonly FPInt64 Rad2Deg = FromRaw(246083499208L);  // 180/π

        public bool Equals(FPInt64 other) => RawValue == other.RawValue;
        public override bool Equals(object obj) => obj is FPInt64 fp && fp.RawValue == RawValue;
        public override int GetHashCode() => RawValue.GetHashCode();
        public int CompareTo(FPInt64 other) => RawValue.CompareTo(other.RawValue);
        public override string ToString() => ToDouble().ToString("F6");
    }

    /// <summary>
    /// Deterministic 2D vector using fixed-point math.
    /// </summary>
    public struct FPVector2 : IEquatable<FPVector2>
    {
        public FPInt64 X;
        public FPInt64 Y;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FPVector2(FPInt64 x, FPInt64 y) { X = x; Y = y; }

        public FPInt64 SqrMagnitude => X * X + Y * Y;
        public FPInt64 Magnitude => FPInt64.Sqrt(SqrMagnitude);

        public FPVector2 Normalized
        {
            get
            {
                var mag = Magnitude;
                if (mag.RawValue == 0) return default;
                return new FPVector2(X / mag, Y / mag);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 Dot(FPVector2 a, FPVector2 b) => a.X * b.X + a.Y * b.Y;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 DistanceSqr(FPVector2 a, FPVector2 b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            return dx * dx + dy * dy;
        }

        public static FPVector2 Lerp(FPVector2 a, FPVector2 b, FPInt64 t) =>
            new FPVector2(FPInt64.Lerp(a.X, b.X, t), FPInt64.Lerp(a.Y, b.Y, t));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector2 operator +(FPVector2 a, FPVector2 b) => new FPVector2(a.X + b.X, a.Y + b.Y);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector2 operator -(FPVector2 a, FPVector2 b) => new FPVector2(a.X - b.X, a.Y - b.Y);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector2 operator *(FPVector2 v, FPInt64 s) => new FPVector2(v.X * s, v.Y * s);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector2 operator *(FPInt64 s, FPVector2 v) => v * s;

        public static readonly FPVector2 Zero = default;
        public static readonly FPVector2 One = new FPVector2(FPInt64.OneValue, FPInt64.OneValue);

        public bool Equals(FPVector2 other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is FPVector2 v && Equals(v);
        public override int GetHashCode() => X.GetHashCode() ^ (Y.GetHashCode() * 397);
        public override string ToString() => $"({X}, {Y})";
    }

    /// <summary>
    /// Deterministic 3D vector using fixed-point math.
    /// </summary>
    public struct FPVector3 : IEquatable<FPVector3>
    {
        public FPInt64 X;
        public FPInt64 Y;
        public FPInt64 Z;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FPVector3(FPInt64 x, FPInt64 y, FPInt64 z) { X = x; Y = y; Z = z; }

        public FPInt64 SqrMagnitude => X * X + Y * Y + Z * Z;
        public FPInt64 Magnitude => FPInt64.Sqrt(SqrMagnitude);

        public FPVector3 Normalized
        {
            get
            {
                var mag = Magnitude;
                if (mag.RawValue == 0) return default;
                return new FPVector3(X / mag, Y / mag, Z / mag);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 Dot(FPVector3 a, FPVector3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        public static FPVector3 Cross(FPVector3 a, FPVector3 b) =>
            new FPVector3(
                a.Y * b.Z - a.Z * b.Y,
                a.Z * b.X - a.X * b.Z,
                a.X * b.Y - a.Y * b.X
            );

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 DistanceSqr(FPVector3 a, FPVector3 b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;
            var dz = a.Z - b.Z;
            return dx * dx + dy * dy + dz * dz;
        }

        public static FPVector3 Lerp(FPVector3 a, FPVector3 b, FPInt64 t) =>
            new FPVector3(
                FPInt64.Lerp(a.X, b.X, t),
                FPInt64.Lerp(a.Y, b.Y, t),
                FPInt64.Lerp(a.Z, b.Z, t));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector3 operator +(FPVector3 a, FPVector3 b) => new FPVector3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector3 operator -(FPVector3 a, FPVector3 b) => new FPVector3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector3 operator *(FPVector3 v, FPInt64 s) => new FPVector3(v.X * s, v.Y * s, v.Z * s);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector3 operator *(FPInt64 s, FPVector3 v) => v * s;

        public static readonly FPVector3 Zero = default;
        public static readonly FPVector3 One = new FPVector3(FPInt64.OneValue, FPInt64.OneValue, FPInt64.OneValue);
        public static readonly FPVector3 Up = new FPVector3(FPInt64.Zero, FPInt64.OneValue, FPInt64.Zero);
        public static readonly FPVector3 Forward = new FPVector3(FPInt64.Zero, FPInt64.Zero, FPInt64.OneValue);
        public static readonly FPVector3 Right = new FPVector3(FPInt64.OneValue, FPInt64.Zero, FPInt64.Zero);

        public bool Equals(FPVector3 other) => X == other.X && Y == other.Y && Z == other.Z;
        public override bool Equals(object obj) => obj is FPVector3 v && Equals(v);
        public override int GetHashCode() => X.GetHashCode() ^ (Y.GetHashCode() * 397) ^ (Z.GetHashCode() * 7919);
        public override string ToString() => $"({X}, {Y}, {Z})";
    }

    /// <summary>
    /// Deterministic seeded PRNG using xoshiro256** algorithm.
    /// Produces identical sequences on all platforms given the same seed.
    /// </summary>
    public sealed class DeterministicRandom
    {
        private ulong _s0, _s1, _s2, _s3;

        public DeterministicRandom(ulong seed)
        {
            // SplitMix64 to initialize state from seed
            _s0 = SplitMix64(ref seed);
            _s1 = SplitMix64(ref seed);
            _s2 = SplitMix64(ref seed);
            _s3 = SplitMix64(ref seed);
        }

        /// <summary>
        /// Returns a deterministic pseudo-random ulong.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ulong NextULong()
        {
            ulong result = RotateLeft(_s1 * 5, 7) * 9;
            ulong t = _s1 << 17;

            _s2 ^= _s0;
            _s3 ^= _s1;
            _s1 ^= _s2;
            _s0 ^= _s3;

            _s2 ^= t;
            _s3 = RotateLeft(_s3, 45);

            return result;
        }

        /// <summary>
        /// Returns a deterministic int in [0, max).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int NextInt(int max)
        {
            if (max <= 0) return 0;
            return (int)(NextULong() % (ulong)max);
        }

        /// <summary>
        /// Returns a deterministic int in [min, max).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int NextInt(int min, int max)
        {
            if (max <= min) return min;
            return min + NextInt(max - min);
        }

        /// <summary>
        /// Returns a deterministic fixed-point value in [0, 1).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FPInt64 NextFP()
        {
            // Use upper 32 bits as fractional part of [0, 1)
            long raw = (long)(NextULong() >> 32);
            return FPInt64.FromRaw(raw);
        }

        /// <summary>
        /// Returns a deterministic fixed-point value in [min, max).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FPInt64 NextFP(FPInt64 min, FPInt64 max)
        {
            return min + NextFP() * (max - min);
        }

        /// <summary>
        /// Save state for replay/rollback.
        /// </summary>
        public (ulong s0, ulong s1, ulong s2, ulong s3) SaveState() => (_s0, _s1, _s2, _s3);

        /// <summary>
        /// Restore state for replay/rollback.
        /// </summary>
        public void RestoreState((ulong s0, ulong s1, ulong s2, ulong s3) state)
        {
            _s0 = state.s0;
            _s1 = state.s1;
            _s2 = state.s2;
            _s3 = state.s3;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong RotateLeft(ulong x, int k) => (x << k) | (x >> (64 - k));

        private static ulong SplitMix64(ref ulong state)
        {
            ulong z = (state += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
