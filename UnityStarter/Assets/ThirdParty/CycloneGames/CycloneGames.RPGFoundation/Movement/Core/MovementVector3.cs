using System;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace CycloneGames.RPGFoundation.Movement.Core
{
    /// <summary>
    /// Engine-independent 3D vector for movement contracts that must stay free of Unity types.
    /// Stored as three consecutive floats so snapshot payloads keep the same size as the previous
    /// three-float representation. Conversions to engine vectors belong to the adapter layer.
    /// </summary>
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
