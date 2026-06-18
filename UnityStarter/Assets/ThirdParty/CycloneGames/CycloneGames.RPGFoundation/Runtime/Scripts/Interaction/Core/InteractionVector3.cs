using System;
using System.Runtime.CompilerServices;

namespace CycloneGames.RPGFoundation.Runtime.Interaction
{
    public readonly struct InteractionVector3 : IEquatable<InteractionVector3>
    {
        public static readonly InteractionVector3 Zero = new InteractionVector3(0f, 0f, 0f);

        public readonly float X;
        public readonly float Y;
        public readonly float Z;

        public InteractionVector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float DistanceSquared(in InteractionVector3 a, in InteractionVector3 b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            float dz = a.Z - b.Z;
            return dx * dx + dy * dy + dz * dz;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float DistanceSquaredTo(in InteractionVector3 other)
        {
            return DistanceSquared(this, other);
        }

        public bool Equals(InteractionVector3 other)
        {
            return X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
        }

        public override bool Equals(object obj)
        {
            return obj is InteractionVector3 other && Equals(other);
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
    }
}
