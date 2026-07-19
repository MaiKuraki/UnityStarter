using System;
using System.Runtime.CompilerServices;

namespace CycloneGames.DeterministicMath
{
    /// <summary>
    /// Q32.32 two-dimensional value vector.
    /// </summary>
    public readonly struct FPVector2 : IEquatable<FPVector2>
    {
        public readonly FPInt64 X;
        public readonly FPInt64 Y;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FPVector2(FPInt64 x, FPInt64 y)
        {
            X = x;
            Y = y;
        }

        /// <summary>Gets the squared magnitude, saturated at <see cref="FPInt64.MaxValue"/> when out of range.</summary>
        public FPInt64 SqrMagnitude => FPMagnitudeUtility.GetSaturatedSquaredMagnitude(X, Y);

        /// <summary>Gets the magnitude, saturated at <see cref="FPInt64.MaxValue"/> when out of range.</summary>
        public FPInt64 Magnitude => FPMagnitudeUtility.GetMagnitude(X, Y);

        /// <summary>Returns a unit vector.</summary>
        /// <exception cref="InvalidOperationException">The vector cannot be normalized.</exception>
        public FPVector2 Normalized
        {
            get
            {
                if (!TryNormalize(out FPVector2 normalized))
                {
                    throw new InvalidOperationException("Cannot normalize a zero vector or a vector outside the supported Q32.32 domain.");
                }

                return normalized;
            }
        }

        /// <summary>Returns a unit vector, or <see cref="Zero"/> when normalization is undefined.</summary>
        public FPVector2 NormalizedOrZero => TryNormalize(out FPVector2 normalized) ? normalized : Zero;

        /// <summary>Attempts to produce a unit vector without throwing.</summary>
        public bool TryNormalize(out FPVector2 normalized)
        {
            if (FPMagnitudeUtility.Normalize(X, Y, out FPInt64 x, out FPInt64 y))
            {
                normalized = new FPVector2(x, y);
                return true;
            }

            normalized = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 Dot(FPVector2 a, FPVector2 b) => a.X * b.X + a.Y * b.Y;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryDot(FPVector2 a, FPVector2 b, out FPInt64 result) =>
            FPMagnitudeUtility.TryDot(a.X, a.Y, b.X, b.Y, out result);

        /// <summary>Returns the squared distance, saturated at <see cref="FPInt64.MaxValue"/> when out of range.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 DistanceSqr(FPVector2 a, FPVector2 b) =>
            FPMagnitudeUtility.GetSaturatedSquaredDistance(a.X, a.Y, b.X, b.Y);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector2 Lerp(FPVector2 a, FPVector2 b, FPInt64 t) =>
            new FPVector2(FPInt64.Lerp(a.X, b.X, t), FPInt64.Lerp(a.Y, b.Y, t));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector2 LerpUnclamped(FPVector2 a, FPVector2 b, FPInt64 t) =>
            new FPVector2(
                FPInt64.LerpUnclamped(a.X, b.X, t),
                FPInt64.LerpUnclamped(a.Y, b.Y, t));

        // ---- Operators ----

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector2 operator +(FPVector2 a, FPVector2 b) => new FPVector2(a.X + b.X, a.Y + b.Y);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector2 operator -(FPVector2 a, FPVector2 b) => new FPVector2(a.X - b.X, a.Y - b.Y);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector2 operator -(FPVector2 v) => new FPVector2(-v.X, -v.Y);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector2 operator *(FPVector2 v, FPInt64 s) => new FPVector2(v.X * s, v.Y * s);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector2 operator *(FPInt64 s, FPVector2 v) => v * s;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector2 operator /(FPVector2 v, FPInt64 s) => new FPVector2(v.X / s, v.Y / s);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(FPVector2 a, FPVector2 b) => a.Equals(b);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(FPVector2 a, FPVector2 b) => !a.Equals(b);

        // ---- Constants ----

        public static readonly FPVector2 Zero = default;
        public static readonly FPVector2 One = new FPVector2(FPInt64.One, FPInt64.One);
        public static readonly FPVector2 Right = new FPVector2(FPInt64.One, FPInt64.Zero);
        public static readonly FPVector2 Up = new FPVector2(FPInt64.Zero, FPInt64.One);

        // ---- Projection / Reflection ----

        /// <summary>Reflect a vector off a surface with the given normal.</summary>
        public static FPVector2 Reflect(FPVector2 v, FPVector2 normal)
        {
            if (!TryReflect(v, normal, out FPVector2 reflected))
            {
                throw new OverflowException("Reflection is outside the Q32.32 range.");
            }

            return reflected;
        }

        public static bool TryReflect(FPVector2 v, FPVector2 normal, out FPVector2 reflected)
        {
            if (!TryDot(v, normal, out FPInt64 dot) ||
                !FPInt64.TryAdd(dot, dot, out FPInt64 twiceDot) ||
                !FPInt64.TryMultiply(normal.X, twiceDot, out FPInt64 offsetX) ||
                !FPInt64.TryMultiply(normal.Y, twiceDot, out FPInt64 offsetY) ||
                !FPInt64.TrySubtract(v.X, offsetX, out FPInt64 x) ||
                !FPInt64.TrySubtract(v.Y, offsetY, out FPInt64 y))
            {
                reflected = default;
                return false;
            }

            reflected = new FPVector2(x, y);
            return true;
        }

        /// <summary>Project a vector onto another vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector2 Project(FPVector2 v, FPVector2 onto)
        {
            if (!TryProject(v, onto, out FPVector2 projection))
            {
                throw new InvalidOperationException("Projection is undefined or outside the Q32.32 range.");
            }

            return projection;
        }

        public static bool TryProject(FPVector2 v, FPVector2 onto, out FPVector2 projection)
        {
            if (!TryDot(onto, onto, out FPInt64 denominator) ||
                denominator.RawValue == 0 ||
                !TryDot(v, onto, out FPInt64 numerator) ||
                !FPInt64.TryDivide(numerator, denominator, out FPInt64 scale) ||
                !FPInt64.TryMultiply(onto.X, scale, out FPInt64 x) ||
                !FPInt64.TryMultiply(onto.Y, scale, out FPInt64 y))
            {
                projection = default;
                return false;
            }

            projection = new FPVector2(x, y);
            return true;
        }

        // ---- Equality ----

        public bool Equals(FPVector2 other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is FPVector2 v && Equals(v);
        public override int GetHashCode() => X.GetHashCode() ^ (Y.GetHashCode() * 397);
        public override string ToString() => $"({X}, {Y})";
    }
}
