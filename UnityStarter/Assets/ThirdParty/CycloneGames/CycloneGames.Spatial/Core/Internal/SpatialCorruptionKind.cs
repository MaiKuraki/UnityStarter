#if DEBUG
namespace CycloneGames.Spatial.Core.Internal
{
    /// <summary>
    /// Structural fault injected by a test to verify that <c>ValidateStructure</c> detects it.
    /// </summary>
    /// <remarks>
    /// Compiled only under the standard <c>DEBUG</c> symbol. Not available in a Release assembly.
    /// </remarks>
    internal enum SpatialCorruptionKind
    {
        /// <summary>Points a live slot's chain link at itself.</summary>
        BreakChainLink = 0,

        /// <summary>Moves a live slot's cell key without moving the slot.</summary>
        MismatchCellKey = 1,

        /// <summary>Increases the free-slot count without freeing a slot.</summary>
        DesynchronizeFreeCount = 2,

        /// <summary>Clears a live slot's generation.</summary>
        ZeroGeneration = 3,

        /// <summary>Empties an occupied bucket's head.</summary>
        OrphanBucketChain = 4
    }
}
#endif
