using System;
using CycloneGames.Hash.Core;

namespace CycloneGames.Spatial.Core
{
    /// <summary>
    /// Integer coordinate of one spatial cell on the three grid axes.
    /// </summary>
    /// <remarks>
    /// The coordinate is produced by <see cref="GridSettings.WorldToCell(float, float, float)"/>, which uses
    /// floor semantics: negative world positions map to smaller cell indices, never toward zero.
    /// Comparison order is X, then Y, then Z so that diagnostic output and any caller-side sorting are stable.
    /// </remarks>
    public readonly struct CellCoordinate : IEquatable<CellCoordinate>, IComparable<CellCoordinate>
    {
        public CellCoordinate(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        /// <summary>Cell index on the X axis.</summary>
        public int X { get; }

        /// <summary>Cell index on the Y axis.</summary>
        public int Y { get; }

        /// <summary>Cell index on the Z axis.</summary>
        public int Z { get; }

        /// <summary>
        /// Returns a new coordinate shifted by the supplied cell offsets.
        /// </summary>
        /// <remarks>Complexity: O(1). Offsets are not range checked; the caller owns the resulting coordinate space.</remarks>
        public CellCoordinate Offset(int dx, int dy, int dz)
        {
            return new CellCoordinate(unchecked(X + dx), unchecked(Y + dy), unchecked(Z + dz));
        }

        /// <summary>
        /// Computes the stable 32-bit hash used by the bucket table.
        /// </summary>
        /// <remarks>
        /// Folds the three axes through <see cref="Fnv1a32"/> in X, Y, Z order. The result is stable across
        /// processes, platforms and runs, which is what makes bucket layout reproducible for a given operation history.
        /// Complexity: O(1).
        /// </remarks>
        public uint ComputeHash()
        {
            unchecked
            {
                uint hash = Fnv1a32.OffsetBasis;
                hash = (hash ^ (uint)X) * 16777619u;
                hash = (hash ^ (uint)Y) * 16777619u;
                hash = (hash ^ (uint)Z) * 16777619u;

                // The bucket index is taken from the low bits, which the final multiply leaves correlated with the
                // input. Folding the high half down costs one shift and one xor.
                return hash ^ (hash >> 16);
            }
        }

        public bool Equals(CellCoordinate other)
        {
            return X == other.X && Y == other.Y && Z == other.Z;
        }

        public override bool Equals(object obj)
        {
            return obj is CellCoordinate other && Equals(other);
        }

        public override int GetHashCode()
        {
            return unchecked((int)ComputeHash());
        }

        /// <summary>Orders by X, then Y, then Z.</summary>
        public int CompareTo(CellCoordinate other)
        {
            int result = X.CompareTo(other.X);
            if (result != 0)
            {
                return result;
            }

            result = Y.CompareTo(other.Y);
            if (result != 0)
            {
                return result;
            }

            return Z.CompareTo(other.Z);
        }

        public static bool operator ==(CellCoordinate left, CellCoordinate right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(CellCoordinate left, CellCoordinate right)
        {
            return !left.Equals(right);
        }

        /// <summary>Returns the coordinate in "(x, y, z)" form. Diagnostic use only; allocates.</summary>
        public override string ToString()
        {
            return "(" + X + ", " + Y + ", " + Z + ")";
        }
    }
}
