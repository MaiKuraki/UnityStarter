namespace CycloneGames.Spatial.Core
{
    /// <summary>
    /// Query surface implemented by every spatial backend.
    /// </summary>
    /// <remarks>
    /// Implementations must not allocate, must write at most the requested limit, and must not omit a candidate that a
    /// conservative broad-phase test accepts. A layer mask of zero returns nothing, and an element layer of zero
    /// matches nothing.
    /// </remarks>
    public interface ISpatialReadOnlyIndex<TItem>
    {
        /// <summary>Elements reachable by a query.</summary>
        int Count { get; }

        /// <summary>
        /// Writes the elements inside the sphere into <paramref name="results"/>, unordered and truncated at the limit.
        /// </summary>
        /// <remarks>Complexity: O(m + k), m = covered cells, k = candidates. O(n) when the cell range exceeds the query budget.</remarks>
        /// <returns>Number of elements written.</returns>
        int QuerySphere(
            float centerX,
            float centerY,
            float centerZ,
            float radius,
            TItem[] results,
            int maxResults,
            uint layerMask);

        /// <summary>
        /// Writes the elements inside the axis-aligned box into <paramref name="results"/>, unordered and truncated at
        /// the limit. Faces are inclusive.
        /// </summary>
        /// <remarks>Complexity: O(m + k).</remarks>
        /// <returns>Number of elements written.</returns>
        int QueryBox(
            float minX,
            float minY,
            float minZ,
            float maxX,
            float maxY,
            float maxZ,
            TItem[] results,
            int maxResults,
            uint layerMask);

        /// <summary>
        /// Writes the closest elements inside the radius, ascending by distance. <c>results[i]</c> is at
        /// <c>squaredDistances[i]</c>.
        /// </summary>
        /// <remarks>
        /// Squared distance is returned so the search avoids <c>sqrt</c>; ordering is identical.
        /// Complexity: O(m + k) plus O(maxResults) per accepted candidate.
        /// </remarks>
        /// <returns>Number of elements written; also the number of valid entries in both buffers.</returns>
        int QueryNearest(
            float centerX,
            float centerY,
            float centerZ,
            float radius,
            int maxResults,
            uint layerMask,
            TItem[] results,
            float[] squaredDistances);
    }
}
