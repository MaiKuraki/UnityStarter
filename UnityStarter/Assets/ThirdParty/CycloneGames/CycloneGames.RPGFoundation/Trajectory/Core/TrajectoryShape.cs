namespace CycloneGames.RPGFoundation.Trajectory.Core
{
    /// <summary>
    /// Sweep volume used by a trace. Core never computes geometry from it: the shape only travels
    /// through <see cref="TrajectoryCastQuery"/> so an adapter can pick the matching cast.
    /// </summary>
    public enum TrajectoryShape : byte
    {
        /// <summary>Infinitely thin line. Radius and extents are ignored.</summary>
        Ray = 0,

        /// <summary>Sphere of <c>Radius</c>.</summary>
        Sphere = 1,

        /// <summary>
        /// Capsule of <c>Radius</c> and total <c>Height</c>, aligned to the world up axis. The caps are
        /// hemispheres, so <c>Height</c> must be at least <c>2 * Radius</c>.
        /// </summary>
        Capsule = 2,

        /// <summary>Axis-aligned box of <c>HalfExtents</c>.</summary>
        Box = 3
    }
}
