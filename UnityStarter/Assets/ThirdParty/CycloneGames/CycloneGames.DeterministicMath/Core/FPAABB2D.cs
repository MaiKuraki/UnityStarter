using System;

namespace CycloneGames.DeterministicMath
{
    /// <summary>Deterministic 2D axis-aligned bounding box.</summary>
    public readonly struct FPAABB2D
    {
        public readonly FPVector2 Min;
        public readonly FPVector2 Max;

        public FPAABB2D(FPVector2 min, FPVector2 max)
        {
            if (min.X > max.X || min.Y > max.Y)
            {
                throw new ArgumentException("Minimum components must not exceed maximum components.", nameof(min));
            }

            Min = min;
            Max = max;
        }

        public FPVector2 Center => new FPVector2(
            FPGeometryUtility.Midpoint(Min.X, Max.X),
            FPGeometryUtility.Midpoint(Min.Y, Max.Y));

        public FPVector2 Extents => new FPVector2(
            FPGeometryUtility.HalfDistance(Min.X, Max.X),
            FPGeometryUtility.HalfDistance(Min.Y, Max.Y));
    }
}
