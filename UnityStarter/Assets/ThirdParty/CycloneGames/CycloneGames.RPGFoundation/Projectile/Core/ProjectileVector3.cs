using System;
using System.Runtime.CompilerServices;

namespace CycloneGames.RPGFoundation.Projectile.Core
{
    public readonly struct ProjectileVector3 : IEquatable<ProjectileVector3>
    {
        public static readonly ProjectileVector3 Zero = default;
        public static readonly ProjectileVector3 Forward = new ProjectileVector3(0f, 0f, 1f);

        public readonly float X;
        public readonly float Y;
        public readonly float Z;

        public ProjectileVector3(float x, float y, float z)
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
        public ProjectileVector3 NormalizedOrZero()
        {
            return NormalizedOrFallback(Zero);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ProjectileVector3 NormalizedOrFallback(ProjectileVector3 fallback)
        {
            float length = Length;
            return length > 0.000001f ? this / length : fallback;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(ProjectileVector3 other)
        {
            return X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
        }

        public override bool Equals(object obj)
        {
            return obj is ProjectileVector3 other && Equals(other);
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
        public static float Dot(ProjectileVector3 left, ProjectileVector3 right)
        {
            return left.X * right.X + left.Y * right.Y + left.Z * right.Z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ProjectileVector3 Lerp(ProjectileVector3 from, ProjectileVector3 to, float t)
        {
            return from + (to - from) * t;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ProjectileVector3 Reflect(ProjectileVector3 vector, ProjectileVector3 normal)
        {
            ProjectileVector3 unitNormal = normal.NormalizedOrZero();
            return vector - unitNormal * (2f * Dot(vector, unitNormal));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(ProjectileVector3 left, ProjectileVector3 right)
        {
            return left.Equals(right);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(ProjectileVector3 left, ProjectileVector3 right)
        {
            return !left.Equals(right);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ProjectileVector3 operator +(ProjectileVector3 left, ProjectileVector3 right)
        {
            return new ProjectileVector3(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ProjectileVector3 operator -(ProjectileVector3 left, ProjectileVector3 right)
        {
            return new ProjectileVector3(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ProjectileVector3 operator -(ProjectileVector3 value)
        {
            return new ProjectileVector3(-value.X, -value.Y, -value.Z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ProjectileVector3 operator *(ProjectileVector3 value, float scalar)
        {
            return new ProjectileVector3(value.X * scalar, value.Y * scalar, value.Z * scalar);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ProjectileVector3 operator *(float scalar, ProjectileVector3 value)
        {
            return value * scalar;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ProjectileVector3 operator /(ProjectileVector3 value, float scalar)
        {
            return new ProjectileVector3(value.X / scalar, value.Y / scalar, value.Z / scalar);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsFiniteValue(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
