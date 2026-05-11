using System;
using System.Runtime.CompilerServices;

namespace CycloneGames.DeterministicMath
{
    /// <summary>
    /// Deterministic 2D vector. All operations are bit-identical across platforms.
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
                if (mag.RawValue == 0) return Zero;
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector2 Lerp(FPVector2 a, FPVector2 b, FPInt64 t) =>
            new FPVector2(FPInt64.Lerp(a.X, b.X, t), FPInt64.Lerp(a.Y, b.Y, t));

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

        // ---- Constants ----

        public static readonly FPVector2 Zero = default;
        public static readonly FPVector2 One = new FPVector2(FPInt64.OneValue, FPInt64.OneValue);
        public static readonly FPVector2 Right = new FPVector2(FPInt64.OneValue, FPInt64.Zero);
        public static readonly FPVector2 Up = new FPVector2(FPInt64.Zero, FPInt64.OneValue);

        // ---- Projection / Reflection ----

        /// <summary>Reflect a vector off a surface with the given normal.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector2 Reflect(FPVector2 v, FPVector2 normal) =>
            v - normal * (2 * Dot(v, normal));

        /// <summary>Project a vector onto another vector.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector2 Project(FPVector2 v, FPVector2 onto)
        {
            var sqrMag = onto.SqrMagnitude;
            if (sqrMag.RawValue == 0) return Zero;
            return onto * (Dot(v, onto) / sqrMag);
        }

        // ---- Equality ----

        public bool Equals(FPVector2 other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is FPVector2 v && Equals(v);
        public override int GetHashCode() => X.GetHashCode() ^ (Y.GetHashCode() * 397);
        public override string ToString() => $"({X}, {Y})";
    }
}
