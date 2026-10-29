namespace CycloneGames.Spatial.Core
{
    /// <summary>
    /// Hard limits that keep a spatial query bounded even when the caller passes an absurd range.
    /// </summary>
    /// <remarks>
    /// A query radius is caller input and may span an unbounded number of cells. Past the budget, queries fall back to
    /// a linear scan: O(n), but bounded.
    /// </remarks>
    public static class SpatialQueryLimits
    {
        /// <summary>
        /// Maximum number of cells a single query may visit before falling back to a linear scan.
        /// Defaults to 65536, matching the budget already used by the AI perception grid in this repository.
        /// </summary>
        public const int MaxCellVisitsPerQuery = 65536;
    }
}
