namespace CycloneGames.Spatial.Core
{
    /// <summary>
    /// Element value accepted by <see cref="SpatialSnapshot{TItem}.Build"/>.
    /// </summary>
    /// <remarks>
    /// Bulk input for <see cref="SpatialSnapshot{TItem}.Build"/>: construction stays one pass plus one sort, with no
    /// per-element bookkeeping. A zero layer is normalised to <see cref="SpatialLayers.Default"/>.
    /// </remarks>
    public readonly struct SpatialEntry<TItem>
    {
        public SpatialEntry(float x, float y, float z, uint layerMask, TItem item)
        {
            X = x;
            Y = y;
            Z = z;
            LayerMask = layerMask == 0u ? SpatialLayers.Default : layerMask;
            Item = item;
        }

        /// <summary>World X.</summary>
        public float X { get; }

        /// <summary>World Y.</summary>
        public float Y { get; }

        /// <summary>World Z.</summary>
        public float Z { get; }

        /// <summary>Layer bits. Zero is normalised to <see cref="SpatialLayers.Default"/>.</summary>
        public uint LayerMask { get; }

        /// <summary>Payload returned by queries.</summary>
        public TItem Item { get; }
    }
}
