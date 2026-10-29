namespace CycloneGames.Spatial.Core.Internal
{
    /// <summary>
    /// Decides whether a cell range is small enough to walk, or whether a query must fall back to a linear scan.
    /// </summary>
    /// <remarks>
    /// Shared by both backends so the fallback triggers at the same size. A radius is caller input and may span an
    /// unbounded number of cells; over budget, queries degrade to a bounded linear scan instead of hanging.
    /// </remarks>
    internal static class CellRangeBudget
    {
        /// <summary>
        /// Returns <c>true</c> when the inclusive range contains no more than
        /// <see cref="SpatialQueryLimits.MaxCellVisitsPerQuery"/> cells.
        /// </summary>
        public static bool IsWithin(in CellCoordinate minCell, in CellCoordinate maxCell)
        {
            long spanX = (long)maxCell.X - minCell.X + 1L;
            long spanY = (long)maxCell.Y - minCell.Y + 1L;
            long spanZ = (long)maxCell.Z - minCell.Z + 1L;

            if (spanX <= 0L || spanY <= 0L || spanZ <= 0L)
            {
                return false;
            }

            const long Limit = SpatialQueryLimits.MaxCellVisitsPerQuery;
            if (spanX > Limit || spanY > Limit || spanZ > Limit)
            {
                return false;
            }

            if (spanX * spanY > Limit)
            {
                return false;
            }

            return spanX * spanY * spanZ <= Limit;
        }
    }
}
