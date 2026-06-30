namespace CycloneGames.RPGFoundation.Trajectory.Core
{
    public enum TrajectoryQueryValidationCode : ushort
    {
        None,
        EmptyCollisionLayerMask,
        NonFiniteOrigin,
        NonFiniteDirection,
        DegenerateDirection,
        InvalidMaxDistance,
        InvalidRadius,
        InvalidReflectionCount,
        InvalidPierceCount,
        InvalidHitCount,
        InvalidIterationCount,
        InvalidSurfaceOffset,
        IterationBudgetMayEndEarly
    }
}
