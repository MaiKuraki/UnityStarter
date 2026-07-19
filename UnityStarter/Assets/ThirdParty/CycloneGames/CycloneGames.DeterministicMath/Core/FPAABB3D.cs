using System;

namespace CycloneGames.DeterministicMath
{
    /// <summary>Deterministic 3D axis-aligned bounding box.</summary>
    public readonly struct FPAABB3D
    {
        public readonly FPVector3 Min;
        public readonly FPVector3 Max;

        public FPAABB3D(FPVector3 min, FPVector3 max)
        {
            if (min.X > max.X || min.Y > max.Y || min.Z > max.Z)
            {
                throw new ArgumentException("Minimum components must not exceed maximum components.", nameof(min));
            }

            Min = min;
            Max = max;
        }

        public FPVector3 Center => new FPVector3(
            FPGeometryUtility.Midpoint(Min.X, Max.X),
            FPGeometryUtility.Midpoint(Min.Y, Max.Y),
            FPGeometryUtility.Midpoint(Min.Z, Max.Z));

        public FPVector3 Extents => new FPVector3(
            FPGeometryUtility.HalfDistance(Min.X, Max.X),
            FPGeometryUtility.HalfDistance(Min.Y, Max.Y),
            FPGeometryUtility.HalfDistance(Min.Z, Max.Z));

        public FPVector3 Size => new FPVector3(
            FPGeometryUtility.DistanceSaturated(Min.X, Max.X),
            FPGeometryUtility.DistanceSaturated(Min.Y, Max.Y),
            FPGeometryUtility.DistanceSaturated(Min.Z, Max.Z));
    }
}
