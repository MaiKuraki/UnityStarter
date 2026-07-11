using System;
using System.Runtime.CompilerServices;

namespace CycloneGames.DeterministicMath
{
    /// <summary>Deterministic 3D oriented bounding box.</summary>
    public readonly struct FPOBB3D
    {
        public readonly FPVector3 Center;
        public readonly FPVector3 HalfExtents;
        public readonly FPQuaternion Orientation;

        public bool IsValid =>
            HalfExtents.X.RawValue >= 0 &&
            HalfExtents.Y.RawValue >= 0 &&
            HalfExtents.Z.RawValue >= 0 &&
            HasNonZeroRaw(Orientation);

        public FPOBB3D(FPVector3 center, FPVector3 halfExtents, FPQuaternion orientation)
        {
            if (halfExtents.X.RawValue < 0 ||
                halfExtents.Y.RawValue < 0 ||
                halfExtents.Z.RawValue < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(halfExtents), "Half extents cannot be negative.");
            }

            if (!orientation.TryNormalize(out FPQuaternion normalizedOrientation))
            {
                throw new ArgumentException("Orientation cannot be a zero quaternion.", nameof(orientation));
            }

            Center = center;
            HalfExtents = halfExtents;
            Orientation = normalizedOrientation;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool HasNonZeroRaw(FPQuaternion orientation) =>
            (orientation.X.RawValue |
             orientation.Y.RawValue |
             orientation.Z.RawValue |
             orientation.W.RawValue) != 0;
    }
}
