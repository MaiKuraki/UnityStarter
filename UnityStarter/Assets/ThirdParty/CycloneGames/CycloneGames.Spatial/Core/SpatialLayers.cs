namespace CycloneGames.Spatial.Core
{
    /// <summary>
    /// Layer bits stored per element and matched by every query.
    /// </summary>
    /// <remarks>
    /// An element matches a query when <c>(elementLayer &amp; queryMask) != 0</c>.
    /// Element layers must be non-zero: zero matches nothing, including <see cref="All"/>.
    /// </remarks>
    public static class SpatialLayers
    {
        /// <summary>Matches every layer.</summary>
        public const uint All = 0xFFFFFFFFu;

        /// <summary>Layer assigned when an insert does not specify one. Bit 0.</summary>
        public const uint Default = 1u;
    }
}
