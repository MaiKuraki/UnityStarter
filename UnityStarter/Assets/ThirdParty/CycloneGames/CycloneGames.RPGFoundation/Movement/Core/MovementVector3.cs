using System;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace CycloneGames.RPGFoundation.Movement.Core
{
    public struct MovementVector3 : IEquatable<MovementVector3>
    {
        public static readonly MovementVector3 Zero = default;
        public static readonly MovementVector3 Up = new MovementVector3(0f, 1f, 0f);

        public float X;
        public float Y;
        public float Z;

        public MovementVector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public float SqrMagnitude
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return X * X + Y * Y + Z * Z; }
        }

        public float Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get { return MathF.Sqrt(SqrMagnitude); }
        }

        public MovementVector3 Normalized
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                float sqr = SqrMagnitude;
                if (sqr < 1E-10f) return Zero;
                float inv = 1f / MathF.Sqrt(sqr);
                return new MovementVector3(X * inv, Y * inv, Z * inv);
            }
        }

        public bool IsFinite
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                return !float.IsNaN(X) && !float.IsInfinity(X)
                       && !float.IsNaN(Y) && !float.IsInfinity(Y)
                       && !float.IsNaN(Z) && !float.IsInfinity(Z);
            }
        }

        public bool IsUnit(float tolerance)
        {
            if (!IsFinite) return false;
            float delta = SqrMagnitude - 1f;
            float limit = tolerance < 0f ? 0f : tolerance;
            return delta >= -limit && delta <= limit;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Dot(MovementVector3 a, MovementVector3 b)
        {
            return a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float SqrDistance(MovementVector3 a, MovementVector3 b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            float dz = a.Z - b.Z;
            return dx * dx + dy * dy + dz * dz;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static MovementVector3 ProjectOn(MovementVector3 value, MovementVector3 unitNormal)
        {
            return unitNormal * Dot(value, unitNormal);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static MovementVector3 operator +(MovementVector3 left, MovementVector3 right)
        {
            return new MovementVector3(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static MovementVector3 operator -(MovementVector3 left, MovementVector3 right)
        {
            return new MovementVector3(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static MovementVector3 operator *(MovementVector3 value, float scale)
        {
            return new MovementVector3(value.X * scale, value.Y * scale, value.Z * scale);
        }

        public bool Equals(MovementVector3 other)
        {
            return X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
        }

        public override bool Equals(object obj)
        {
            return obj is MovementVector3 other && Equals(other);
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
            return string.Format(CultureInfo.InvariantCulture, "({0}, {1}, {2})", X, Y, Z);
        }
    }
}
