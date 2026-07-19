using System;

namespace CycloneGames.DeterministicMath
{
    /// <summary>Deterministic 3D sphere.</summary>
    public readonly struct FPSphere
    {
        public readonly FPVector3 Center;
        public readonly FPInt64 Radius;

        public FPSphere(FPVector3 center, FPInt64 radius)
        {
            if (radius.RawValue < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(radius), "Radius cannot be negative.");
            }

            Center = center;
            Radius = radius;
        }
    }
}
