namespace CycloneGames.Spatial.Core
{
    /// <summary>
    /// Behaviour when an insert arrives with every slot occupied.
    /// </summary>
    public enum SpatialCapacityPolicy
    {
        /// <summary>
        /// Reject the insert and return <c>false</c>. Use on a fixed-rate path where a frame must not pay for an
        /// O(n) reallocation; the caller reserves capacity up front.
        /// </summary>
        Fixed = 0,

        /// <summary>
        /// Double the slot arrays and accept the insert. Amortized O(1), counted in
        /// <see cref="SpatialStatistics.GrowthCount"/>.
        /// </summary>
        Grow = 1
    }
}
