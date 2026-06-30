using System;
using System.Runtime.CompilerServices;

namespace CycloneGames.RPGFoundation.Trajectory.Core
{
    public readonly struct TrajectoryVector3 : IEquatable<TrajectoryVector3>
    {
        public static readonly TrajectoryVector3 Zero = default;
        public static readonly TrajectoryVector3 Forward = new TrajectoryVector3(0f, 0f, 1f);
        public static readonly TrajectoryVector3 Up = new TrajectoryVector3(0f, 1f, 0f);
        public static readonly TrajectoryVector3 Right = new TrajectoryVector3(1f, 0f, 0f);

        public readonly float X;
        public readonly float Y;
        public readonly float Z;

        public TrajectoryVector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public float Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return (float)Math.Sqrt(LengthSquared);
            }
        }

        public float LengthSquared
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return X * X + Y * Y + Z * Z;
            }
        }

        public bool IsFinite
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return IsFiniteValue(X) && IsFiniteValue(Y) && IsFiniteValue(Z);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TrajectoryVector3 NormalizedOrZero()
        {
            return NormalizedOrFallback(Zero);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public TrajectoryVector3 NormalizedOrFallback(TrajectoryVector3 fallback)
        {
            float length = Length;
            return length > 0.000001f ? this / length : fallback;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(TrajectoryVector3 other)
        {
            return X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
        }

        public override bool Equals(object obj)
        {
            return obj is TrajectoryVector3 other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = X.GetHashCode();
                hash = (hash * 397) ^ Y.GetHashCode();
                hash = (hash * 397) ^ Z.GetHashCode();
                return hash;
            }
        }

        public override string ToString()
        {
            return $"({X}, {Y}, {Z})";
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Dot(TrajectoryVector3 left, TrajectoryVector3 right)
        {
            return left.X * right.X + left.Y * right.Y + left.Z * right.Z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TrajectoryVector3 Reflect(TrajectoryVector3 vector, TrajectoryVector3 normal)
        {
            TrajectoryVector3 unitNormal = normal.NormalizedOrZero();
            return vector - unitNormal * (2f * Dot(vector, unitNormal));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(TrajectoryVector3 left, TrajectoryVector3 right)
        {
            return left.Equals(right);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(TrajectoryVector3 left, TrajectoryVector3 right)
        {
            return !left.Equals(right);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TrajectoryVector3 operator +(TrajectoryVector3 left, TrajectoryVector3 right)
        {
            return new TrajectoryVector3(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TrajectoryVector3 operator -(TrajectoryVector3 left, TrajectoryVector3 right)
        {
            return new TrajectoryVector3(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TrajectoryVector3 operator -(TrajectoryVector3 value)
        {
            return new TrajectoryVector3(-value.X, -value.Y, -value.Z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TrajectoryVector3 operator *(TrajectoryVector3 value, float scalar)
        {
            return new TrajectoryVector3(value.X * scalar, value.Y * scalar, value.Z * scalar);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TrajectoryVector3 operator *(float scalar, TrajectoryVector3 value)
        {
            return value * scalar;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TrajectoryVector3 operator /(TrajectoryVector3 value, float scalar)
        {
            return new TrajectoryVector3(value.X / scalar, value.Y / scalar, value.Z / scalar);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsFiniteValue(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
