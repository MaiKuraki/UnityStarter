using System;

namespace CycloneGames.DeterministicMath
{
    /// <summary>Deterministic 2D circle.</summary>
    public readonly struct FPCircle
    {
        public readonly FPVector2 Center;
        public readonly FPInt64 Radius;

        public FPCircle(FPVector2 center, FPInt64 radius)
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
