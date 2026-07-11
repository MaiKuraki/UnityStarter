using System;
using System.Runtime.CompilerServices;

namespace CycloneGames.DeterministicMath
{
    /// <summary>
    /// Q32.32 three-dimensional value vector with Y-up basis constants.
    /// </summary>
    public readonly struct FPVector3 : IEquatable<FPVector3>
    {
        public readonly FPInt64 X;
        public readonly FPInt64 Y;
        public readonly FPInt64 Z;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FPVector3(FPInt64 x, FPInt64 y, FPInt64 z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        /// <summary>Gets the squared magnitude, saturated at <see cref="FPInt64.MaxValue"/> when out of range.</summary>
        public FPInt64 SqrMagnitude => FPMagnitudeUtility.GetSaturatedSquaredMagnitude(X, Y, Z);

        /// <summary>Gets the magnitude, saturated at <see cref="FPInt64.MaxValue"/> when out of range.</summary>
        public FPInt64 Magnitude => FPMagnitudeUtility.GetMagnitude(X, Y, Z);

        /// <summary>Returns a unit vector.</summary>
        /// <exception cref="InvalidOperationException">The vector cannot be normalized.</exception>
        public FPVector3 Normalized
        {
            get
            {
                if (!TryNormalize(out FPVector3 normalized))
                {
                    throw new InvalidOperationException("Cannot normalize a zero vector or a vector outside the supported Q32.32 domain.");
                }

                return normalized;
            }
        }

        /// <summary>Returns a unit vector, or <see cref="Zero"/> when normalization is undefined.</summary>
        public FPVector3 NormalizedOrZero => TryNormalize(out FPVector3 normalized) ? normalized : Zero;

        /// <summary>Attempts to produce a unit vector without throwing.</summary>
        public bool TryNormalize(out FPVector3 normalized)
        {
            if (FPMagnitudeUtility.Normalize(
                    X,
                    Y,
                    Z,
                    out FPInt64 x,
                    out FPInt64 y,
                    out FPInt64 z))
            {
                normalized = new FPVector3(x, y, z);
                return true;
            }

            normalized = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 Dot(FPVector3 a, FPVector3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryDot(FPVector3 a, FPVector3 b, out FPInt64 result) =>
            FPMagnitudeUtility.TryDot(a, b, out result);

        public static FPVector3 Cross(FPVector3 a, FPVector3 b) =>
            new FPVector3(
                a.Y * b.Z - a.Z * b.Y,
                a.Z * b.X - a.X * b.Z,
                a.X * b.Y - a.Y * b.X
            );

        /// <summary>Returns the squared distance, saturated at <see cref="FPInt64.MaxValue"/> when out of range.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 DistanceSqr(FPVector3 a, FPVector3 b) =>
            FPMagnitudeUtility.GetSaturatedSquaredDistance(a.X, a.Y, a.Z, b.X, b.Y, b.Z);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector3 Lerp(FPVector3 a, FPVector3 b, FPInt64 t) =>
            new FPVector3(
                FPInt64.Lerp(a.X, b.X, t),
                FPInt64.Lerp(a.Y, b.Y, t),
                FPInt64.Lerp(a.Z, b.Z, t));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector3 LerpUnclamped(FPVector3 a, FPVector3 b, FPInt64 t) =>
            new FPVector3(
                FPInt64.LerpUnclamped(a.X, b.X, t),
                FPInt64.LerpUnclamped(a.Y, b.Y, t),
                FPInt64.LerpUnclamped(a.Z, b.Z, t));

        // ---- Operators ----

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector3 operator +(FPVector3 a, FPVector3 b) => new FPVector3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector3 operator -(FPVector3 a, FPVector3 b) => new FPVector3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector3 operator -(FPVector3 v) => new FPVector3(-v.X, -v.Y, -v.Z);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector3 operator *(FPVector3 v, FPInt64 s) => new FPVector3(v.X * s, v.Y * s, v.Z * s);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector3 operator *(FPInt64 s, FPVector3 v) => v * s;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector3 operator /(FPVector3 v, FPInt64 s) => new FPVector3(v.X / s, v.Y / s, v.Z / s);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(FPVector3 a, FPVector3 b) => a.Equals(b);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(FPVector3 a, FPVector3 b) => !a.Equals(b);

        // ---- Constants ----

        public static readonly FPVector3 Zero = default;
        public static readonly FPVector3 One = new FPVector3(FPInt64.One, FPInt64.One, FPInt64.One);
        public static readonly FPVector3 Up = new FPVector3(FPInt64.Zero, FPInt64.One, FPInt64.Zero);
        public static readonly FPVector3 Down = new FPVector3(FPInt64.Zero, FPInt64.MinusOne, FPInt64.Zero);
        public static readonly FPVector3 Forward = new FPVector3(FPInt64.Zero, FPInt64.Zero, FPInt64.One);
        public static readonly FPVector3 Back = new FPVector3(FPInt64.Zero, FPInt64.Zero, FPInt64.MinusOne);
        public static readonly FPVector3 Right = new FPVector3(FPInt64.One, FPInt64.Zero, FPInt64.Zero);
        public static readonly FPVector3 Left = new FPVector3(FPInt64.MinusOne, FPInt64.Zero, FPInt64.Zero);

        // ---- Projection / Reflection ----

        /// <summary>Reflect a vector off a surface with the given normal.</summary>
        public static FPVector3 Reflect(FPVector3 v, FPVector3 normal)
        {
            if (!TryReflect(v, normal, out FPVector3 reflected))
            {
                throw new OverflowException("Reflection is outside the Q32.32 range.");
            }

            return reflected;
        }

        public static bool TryReflect(FPVector3 v, FPVector3 normal, out FPVector3 reflected)
        {
            if (!TryDot(v, normal, out FPInt64 dot) ||
                !FPInt64.TryAdd(dot, dot, out FPInt64 twiceDot) ||
                !FPInt64.TryMultiply(normal.X, twiceDot, out FPInt64 offsetX) ||
                !FPInt64.TryMultiply(normal.Y, twiceDot, out FPInt64 offsetY) ||
                !FPInt64.TryMultiply(normal.Z, twiceDot, out FPInt64 offsetZ) ||
                !FPInt64.TrySubtract(v.X, offsetX, out FPInt64 x) ||
                !FPInt64.TrySubtract(v.Y, offsetY, out FPInt64 y) ||
                !FPInt64.TrySubtract(v.Z, offsetZ, out FPInt64 z))
            {
                reflected = default;
                return false;
            }

            reflected = new FPVector3(x, y, z);
            return true;
        }

        /// <summary>Project a vector onto another vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector3 Project(FPVector3 v, FPVector3 onto)
        {
            if (!TryProject(v, onto, out FPVector3 projection))
            {
                throw new InvalidOperationException("Projection is undefined or outside the Q32.32 range.");
            }

            return projection;
        }

        public static bool TryProject(FPVector3 v, FPVector3 onto, out FPVector3 projection)
        {
            if (!TryDot(onto, onto, out FPInt64 denominator) ||
                denominator.RawValue == 0 ||
                !TryDot(v, onto, out FPInt64 numerator) ||
                !FPInt64.TryDivide(numerator, denominator, out FPInt64 scale) ||
                !FPInt64.TryMultiply(onto.X, scale, out FPInt64 x) ||
                !FPInt64.TryMultiply(onto.Y, scale, out FPInt64 y) ||
                !FPInt64.TryMultiply(onto.Z, scale, out FPInt64 z))
            {
                projection = default;
                return false;
            }

            projection = new FPVector3(x, y, z);
            return true;
        }

        // ---- Equality ----

        public bool Equals(FPVector3 other) => X == other.X && Y == other.Y && Z == other.Z;
        public override bool Equals(object obj) => obj is FPVector3 v && Equals(v);
        public override int GetHashCode() => X.GetHashCode() ^ (Y.GetHashCode() * 397) ^ (Z.GetHashCode() * 7919);
        public override string ToString() => $"({X}, {Y}, {Z})";
    }
}
