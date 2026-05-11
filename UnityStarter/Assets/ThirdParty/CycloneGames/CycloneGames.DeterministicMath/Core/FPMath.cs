using System;
using System.Runtime.CompilerServices;

namespace CycloneGames.DeterministicMath
{
    /// <summary>
    /// Deterministic trigonometric and mathematical functions using the CORDIC algorithm.
    /// All operations produce bit-identical results across all platforms.
    /// <para>
    /// CORDIC uses only shifts and additions - no floating-point, no table-size-dependent
    /// precision variance. 32 iterations give ~2e-10 precision, matching Q32.32 resolution.
    /// </para>
    /// </summary>
    public static class FPMath
    {
        // ---- CORDIC atan(2^-i) table, 32 entries, Q32.32 raw values ----

        private static readonly long[] AtanTable =
        {
            3373404705L, // atan(2^0)  approx 0.785398 rad (45 degrees)
            1991439726L, // atan(2^-1) approx 0.463648 rad
            1052154645L, // atan(2^-2) approx 0.244979 rad
            534153212L,  // atan(2^-3) approx 0.124355 rad
            268107690L,  // atan(2^-4)
            134178680L,  // atan(2^-5)
            67102367L,   // atan(2^-6)
            33552339L,   // atan(2^-7)
            16776807L,   // atan(2^-8)
            8388600L,    // atan(2^-9)
            4194304L,    // atan(2^-10)
            2097152L,    // atan(2^-11)
            1048576L,    // atan(2^-12)
            524288L,     // atan(2^-13)
            262144L,     // atan(2^-14)
            131072L,     // atan(2^-15)
            65536L,      // atan(2^-16)
            32768L,      // atan(2^-17)
            16384L,      // atan(2^-18)
            8192L,       // atan(2^-19)
            4096L,       // atan(2^-20)
            2048L,       // atan(2^-21)
            1024L,       // atan(2^-22)
            512L,        // atan(2^-23)
            256L,        // atan(2^-24)
            128L,        // atan(2^-25)
            64L,         // atan(2^-26)
            32L,         // atan(2^-27)
            16L,         // atan(2^-28)
            8L,          // atan(2^-29)
            4L,          // atan(2^-30)
            2L,          // atan(2^-31)
        };

        // CORDIC rotation starts pre-scaled by K to cancel the iterative gain.
        private const long CordicK_Raw = 2608131496L;

        // ---- Public Trig API ----

        /// <summary>Sine. Precision ~2e-10.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 Sin(FPInt64 radians)
        {
            CordicSinCos(radians, out var _, out var sin);
            return sin;
        }

        /// <summary>Cosine. Precision ~2e-10.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 Cos(FPInt64 radians)
        {
            CordicSinCos(radians, out var cos, out var _);
            return cos;
        }

        /// <summary>
        /// Computes both Sin and Cos in a single CORDIC pass.
        /// Call this instead of two separate calls for half the cost.
        /// </summary>
        public static void SinCos(FPInt64 radians, out FPInt64 sin, out FPInt64 cos)
        {
            CordicSinCos(radians, out cos, out sin);
        }

        /// <summary>Tangent. Computed as Sin/Cos.</summary>
        public static FPInt64 Tan(FPInt64 radians)
        {
            CordicSinCos(radians, out var cos, out var sin);
            if (cos.RawValue == 0)
            {
                // asymptote - return large value with correct sign
                return sin.RawValue >= 0
                    ? FPInt64.FromRaw(long.MaxValue)
                    : FPInt64.FromRaw(long.MinValue);
            }
            return sin / cos;
        }

        /// <summary>Arc tangent of y/x with quadrant awareness. Returns radians in [-Pi, Pi].</summary>
        public static FPInt64 Atan2(FPInt64 y, FPInt64 x)
        {
            if (y.RawValue == 0)
            {
                return x.RawValue < 0 ? FPInt64.Pi : FPInt64.Zero;
            }
            if (x.RawValue == 0)
            {
                if (y.RawValue > 0) return FPInt64.HalfPi;
                if (y.RawValue < 0) return -FPInt64.HalfPi;
            }
            // CORDIC vectoring mode
            var result = CordicVec(y, x);
            return result;
        }

        /// <summary>Arc tangent. Returns radians in [-HalfPi, HalfPi].</summary>
        public static FPInt64 Atan(FPInt64 v)
        {
            return Atan2(v, FPInt64.OneValue);
        }

        /// <summary>Arc sine. Domain: [-1, 1].</summary>
        public static FPInt64 Asin(FPInt64 x)
        {
            if (x.RawValue > FPInt64.OneValue.RawValue || x.RawValue < -FPInt64.OneValue.RawValue)
            {
                x = FPInt64.Clamp(x, -FPInt64.OneValue, FPInt64.OneValue);
            }
            return Atan2(x, FPInt64.Sqrt(FPInt64.OneValue - x * x));
        }

        /// <summary>Arc cosine. Domain: [-1, 1].</summary>
        public static FPInt64 Acos(FPInt64 x)
        {
            if (x.RawValue > FPInt64.OneValue.RawValue || x.RawValue < -FPInt64.OneValue.RawValue)
            {
                x = FPInt64.Clamp(x, -FPInt64.OneValue, FPInt64.OneValue);
            }
            return FPInt64.HalfPi - Atan2(x, FPInt64.Sqrt(FPInt64.OneValue - x * x));
        }

        /// <summary>Wrap angle to [-Pi, Pi].</summary>
        public static FPInt64 NormalizeAngle(FPInt64 radians)
        {
            // Use integer modulo on raw values for deterministic normalization
            long twoPiRaw = FPInt64.TwoPi.RawValue;
            long raw = radians.RawValue % twoPiRaw;
            if (raw > FPInt64.Pi.RawValue) raw -= twoPiRaw;
            else if (raw < -FPInt64.Pi.RawValue) raw += twoPiRaw;
            return FPInt64.FromRaw(raw);
        }

        /// <summary>Wrap angle to [0, TwoPi).</summary>
        public static FPInt64 NormalizeAnglePositive(FPInt64 radians)
        {
            long twoPiRaw = FPInt64.TwoPi.RawValue;
            long raw = radians.RawValue % twoPiRaw;
            if (raw < 0) raw += twoPiRaw;
            return FPInt64.FromRaw(raw);
        }

        // ---- Internal CORDIC Core ----

        /// <summary>
        /// CORDIC rotation mode: compute (cos, sin) of an angle.
        /// Returns cos in the first out param, sin in the second.
        /// Angle must be in [-HalfPi, HalfPi] for full precision.
        /// </summary>
        private static void CordicRotation(long angleRaw, out long cosRaw, out long sinRaw)
        {
            // Start at (1/K, 0); 32 iterations converge to (cos, sin)
            long x = CordicK_Raw;
            long y = 0;
            long z = angleRaw;

            for (int i = 0; i < 32; i++)
            {
                long atanI = AtanTable[i];
                if (z >= 0)
                {
                    long xNew = x - (y >> i);
                    long yNew = y + (x >> i);
                    x = xNew;
                    y = yNew;
                    z -= atanI;
                }
                else
                {
                    long xNew = x + (y >> i);
                    long yNew = y - (x >> i);
                    x = xNew;
                    y = yNew;
                    z += atanI;
                }
            }

            cosRaw = x;
            sinRaw = y;
        }

        /// <summary>
        /// CORDIC vectoring mode: compute Atan2(y, x).
        /// Returns the angle (in radians, raw value) from the positive x-axis.
        /// </summary>
        private static FPInt64 CordicVec(FPInt64 y, FPInt64 x)
        {
            long xRaw = x.RawValue;
            long yRaw = y.RawValue;
            long originalYRaw = yRaw;
            long z = 0;

            // Handle sign and quadrant mapping.
            // CORDIC vectoring mode converges for x > 0.
            // If x < 0, we rotate into the right half-plane and adjust at the end.
            FPInt64 resultOffset = FPInt64.Zero;

            if (xRaw < 0)
            {
                // Reflect across y-axis to make x positive
                xRaw = -xRaw;
                yRaw = -yRaw;
                if (originalYRaw >= 0)
                    resultOffset = FPInt64.Pi;
                else
                    resultOffset = -FPInt64.Pi;
            }

            // CORDIC iterations: drive y toward zero, accumulate angle in z
            for (int i = 0; i < 32; i++)
            {
                long atanI = AtanTable[i];
                if (yRaw < 0)
                {
                    // Rotate counter-clockwise
                    long xNew = xRaw - (yRaw >> i);
                    long yNew = yRaw + (xRaw >> i);
                    xRaw = xNew;
                    yRaw = yNew;
                    z -= atanI;
                }
                else
                {
                    // Rotate clockwise
                    long xNew = xRaw + (yRaw >> i);
                    long yNew = yRaw - (xRaw >> i);
                    xRaw = xNew;
                    yRaw = yNew;
                    z += atanI;
                }
            }

            return FPInt64.FromRaw(z) + resultOffset;
        }

        /// <summary>
        /// Full Sin/Cos with quadrant mapping.
        /// </summary>
        private static FPInt64 CordicSinCos(FPInt64 radians, out FPInt64 cos, out FPInt64 sin)
        {
            // Normalize to [-Pi, Pi]
            long raw = radians.RawValue;
            long twoPiRaw = FPInt64.TwoPi.RawValue;
            raw = raw % twoPiRaw;
            if (raw > FPInt64.Pi.RawValue) raw -= twoPiRaw;
            else if (raw < -FPInt64.Pi.RawValue) raw += twoPiRaw;

            if (raw == 0)
            {
                cos = FPInt64.OneValue;
                sin = FPInt64.Zero;
                return FPInt64.Zero;
            }

            if (raw == FPInt64.HalfPi.RawValue)
            {
                cos = FPInt64.Zero;
                sin = FPInt64.OneValue;
                return FPInt64.Zero;
            }

            if (raw == -FPInt64.HalfPi.RawValue)
            {
                cos = FPInt64.Zero;
                sin = FPInt64.MinusOne;
                return FPInt64.Zero;
            }

            if (raw == FPInt64.Pi.RawValue || raw == -FPInt64.Pi.RawValue)
            {
                cos = FPInt64.MinusOne;
                sin = FPInt64.Zero;
                return FPInt64.Zero;
            }

            // Map to [0, TwoPi) for quadrant detection
            bool neg = raw < 0;
            if (neg) raw = -raw;

            long halfPi = FPInt64.HalfPi.RawValue;
            long pi = FPInt64.Pi.RawValue;

            long angleRaw = raw;
            int quadrant = 0;

            if (angleRaw <= halfPi)
            {
                quadrant = 0;
            }
            else if (angleRaw <= pi)
            {
                quadrant = 1;
                angleRaw = pi - angleRaw;
            }
            else if (angleRaw <= halfPi + pi)
            {
                quadrant = 2;
                angleRaw = angleRaw - pi;
            }
            else
            {
                quadrant = 3;
                angleRaw = twoPiRaw - angleRaw;
            }

            if (neg)
            {
                // sin(-theta) = -sin(theta), cos(-theta) = cos(theta)
                CordicRotation(angleRaw, out var cosRaw, out var sinRaw);
                long cosVal = quadrant switch
                {
                    0 => cosRaw,
                    1 => -cosRaw,
                    2 => -cosRaw,
                    _ => cosRaw,
                };
                long sinVal = quadrant switch
                {
                    0 => -sinRaw,
                    1 => -sinRaw,
                    2 => sinRaw,
                    _ => sinRaw,
                };
                cos = FPInt64.FromRaw(cosVal);
                sin = FPInt64.FromRaw(sinVal);
            }
            else
            {
                CordicRotation(angleRaw, out var cosRaw, out var sinRaw);
                long cosVal = quadrant switch
                {
                    0 => cosRaw,
                    1 => -cosRaw,
                    2 => -cosRaw,
                    _ => cosRaw,
                };
                long sinVal = quadrant switch
                {
                    0 => sinRaw,
                    1 => sinRaw,
                    2 => -sinRaw,
                    _ => -sinRaw,
                };
                cos = FPInt64.FromRaw(cosVal);
                sin = FPInt64.FromRaw(sinVal);
            }

            return FPInt64.Zero; // dummy return, caller uses out params
        }
    }
}
