namespace CycloneGames.Spatial.Core.Internal
{
    /// <summary>
    /// Bounded insertion into an ascending-by-squared-distance result buffer.
    /// </summary>
    /// <remarks>
    /// Shared by both backends so nearest-order semantics are identical. Insertion sort over a small, already sorted
    /// buffer. <paramref name="limit"/> must be greater than zero.
    /// </remarks>
    internal static class NearestBuffer
    {
        /// <summary>
        /// Inserts one candidate if it belongs in the buffer.
        /// </summary>
        /// <returns>The new number of valid entries, never greater than <paramref name="limit"/>.</returns>
        public static int Insert<TItem>(
            int count,
            int limit,
            float distanceSquared,
            TItem item,
            TItem[] results,
            float[] squaredDistances)
        {
            int index;
            if (count >= limit)
            {
                if (distanceSquared >= squaredDistances[limit - 1])
                {
                    return count;
                }

                index = limit - 1;
            }
            else
            {
                index = count;
                count++;
            }

            while (index > 0 && squaredDistances[index - 1] > distanceSquared)
            {
                squaredDistances[index] = squaredDistances[index - 1];
                results[index] = results[index - 1];
                index--;
            }

            squaredDistances[index] = distanceSquared;
            results[index] = item;
            return count;
        }
    }
}
