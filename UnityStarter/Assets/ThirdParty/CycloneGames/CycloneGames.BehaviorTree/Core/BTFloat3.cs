using System;
using System.Globalization;

namespace CycloneGames.BehaviorTree.Core
{
    /// <summary>
    /// Engine-independent three-component vector used by the blackboard and its wire format.
    /// Host layers convert to and from their own vector type through extension methods.
    /// </summary>
    public readonly struct BTFloat3 : IEquatable<BTFloat3>
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;

        public BTFloat3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static readonly BTFloat3 Zero = new BTFloat3(0f, 0f, 0f);

        public bool Equals(BTFloat3 other)
        {
            return X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
        }

        public override bool Equals(object obj)
        {
            return obj is BTFloat3 other && Equals(other);
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

        /// <summary>
        /// Bitwise comparison. Used for change detection so that NaN values and signed zero
        /// follow the same rules as the encoded wire payload.
        /// </summary>
        public static bool BitEquals(BTFloat3 left, BTFloat3 right)
        {
            return BitConverter.SingleToInt32Bits(left.X) == BitConverter.SingleToInt32Bits(right.X)
                && BitConverter.SingleToInt32Bits(left.Y) == BitConverter.SingleToInt32Bits(right.Y)
                && BitConverter.SingleToInt32Bits(left.Z) == BitConverter.SingleToInt32Bits(right.Z);
        }

        public static bool operator ==(BTFloat3 left, BTFloat3 right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(BTFloat3 left, BTFloat3 right)
        {
            return !left.Equals(right);
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "({0}, {1}, {2})", X, Y, Z);
        }
    }
}
