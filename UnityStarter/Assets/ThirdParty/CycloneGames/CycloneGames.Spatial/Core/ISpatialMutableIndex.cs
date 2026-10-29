namespace CycloneGames.Spatial.Core
{
    /// <summary>
    /// Query plus mutation surface for backends that maintain a live index.
    /// </summary>
    /// <remarks>
    /// Read-only backends implement <see cref="ISpatialReadOnlyIndex{TItem}"/> only, so a read-only consumer never has
    /// to guard against <c>NotSupportedException</c>.
    /// </remarks>
    public interface ISpatialMutableIndex<TItem> : ISpatialReadOnlyIndex<TItem>
    {
        /// <summary>
        /// Adds an element at the supplied position.
        /// </summary>
        /// <remarks>Complexity: amortized O(1); O(n) when the slot arrays grow or the cell bucket table rebuilds.</remarks>
        /// <param name="layerMask">
        /// Layer bits for the element. Zero is normalised to <see cref="SpatialLayers.Default"/>.
        /// </param>
        /// <returns><c>false</c> when the position is not finite, the index is disposed, or capacity is exhausted.</returns>
        bool TryInsert(TItem item, float x, float y, float z, uint layerMask, out SpatialHandle handle);

        /// <summary>
        /// Moves an element to a new position, keeping its layer.
        /// </summary>
        /// <remarks>Complexity: O(1) within one cell, O(k) across cells.</remarks>
        /// <returns><c>false</c> when the handle is stale or the position is not finite.</returns>
        bool TryUpdate(SpatialHandle handle, float x, float y, float z);

        /// <summary>
        /// Removes an element and recycles its slot.
        /// </summary>
        /// <remarks>Complexity: O(1). The slot generation advances, so the handle cannot match a later occupant.</remarks>
        /// <returns><c>false</c> when the handle is stale.</returns>
        bool TryRemove(SpatialHandle handle);

        /// <summary>
        /// Removes every element while keeping the allocated capacity.
        /// </summary>
        /// <remarks>Complexity: O(occupied slots). Live generations advance, invalidating earlier handles.</remarks>
        void Clear();
    }
}
