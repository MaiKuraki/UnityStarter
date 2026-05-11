using System;
using System.Runtime.CompilerServices;

namespace CycloneGames.DeterministicMath
{
    /// <summary>
    /// Deterministic 3D vector. All operations are bit-identical across platforms.
    /// Follows Unity's left-handed Y-up coordinate convention.
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
                if (mag.RawValue == 0) return Zero;
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector3 Lerp(FPVector3 a, FPVector3 b, FPInt64 t) =>
            new FPVector3(
                FPInt64.Lerp(a.X, b.X, t),
                FPInt64.Lerp(a.Y, b.Y, t),
                FPInt64.Lerp(a.Z, b.Z, t));

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

        // ---- Constants ----

        public static readonly FPVector3 Zero = default;
        public static readonly FPVector3 One = new FPVector3(FPInt64.OneValue, FPInt64.OneValue, FPInt64.OneValue);
        public static readonly FPVector3 Up = new FPVector3(FPInt64.Zero, FPInt64.OneValue, FPInt64.Zero);
        public static readonly FPVector3 Down = new FPVector3(FPInt64.Zero, FPInt64.MinusOne, FPInt64.Zero);
        public static readonly FPVector3 Forward = new FPVector3(FPInt64.Zero, FPInt64.Zero, FPInt64.OneValue);
        public static readonly FPVector3 Back = new FPVector3(FPInt64.Zero, FPInt64.Zero, FPInt64.MinusOne);
        public static readonly FPVector3 Right = new FPVector3(FPInt64.OneValue, FPInt64.Zero, FPInt64.Zero);
        public static readonly FPVector3 Left = new FPVector3(FPInt64.MinusOne, FPInt64.Zero, FPInt64.Zero);

        // ---- Projection / Reflection ----

        /// <summary>Reflect a vector off a surface with the given normal.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector3 Reflect(FPVector3 v, FPVector3 normal) =>
            v - normal * (2 * Dot(v, normal));

        /// <summary>Project a vector onto another vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector3 Project(FPVector3 v, FPVector3 onto)
        {
            var sqrMag = onto.SqrMagnitude;
            if (sqrMag.RawValue == 0) return Zero;
            return onto * (Dot(v, onto) / sqrMag);
        }

        // ---- Equality ----

        public bool Equals(FPVector3 other) => X == other.X && Y == other.Y && Z == other.Z;
        public override bool Equals(object obj) => obj is FPVector3 v && Equals(v);
        public override int GetHashCode() => X.GetHashCode() ^ (Y.GetHashCode() * 397) ^ (Z.GetHashCode() * 7919);
        public override string ToString() => $"({X}, {Y}, {Z})";
    }
}
