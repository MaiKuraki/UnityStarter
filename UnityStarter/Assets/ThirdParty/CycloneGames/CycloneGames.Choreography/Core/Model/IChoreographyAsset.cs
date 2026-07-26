using System.Collections.Generic;

namespace CycloneGames.Choreography.Core
{
    /// <summary>
    /// The top-level, provider-agnostic contract for a playable choreography. Implementations are typically
    /// backed by authored data (e.g. a Unity <c>ScriptableObject</c> in the interaction layer) but the Core
    /// contract must never expose engine types. Assets are treated as immutable playback data.
    /// </summary>
    public interface IChoreographyAsset
    {
        /// <summary>Stable identifier for this choreography (used for diagnostics, pooling, and lookups).</summary>
        string Id { get; }

        /// <summary>Total playable length in seconds, equal to the sum of section durations.</summary>
        double TotalDuration { get; }

        /// <summary>Ordered sections that make up the timeline. Never null.</summary>
        IReadOnlyList<ChoreographySection> Sections { get; }

        /// <summary>
        /// Appends every distinct resource reference required to play this choreography into
        /// <paramref name="results"/> and returns the number of references appended.
        /// The caller owns and provides the list, which keeps this call allocation-free on warm paths.
        /// Implementations must not clear the list; they only append.
        /// </summary>
        int CollectResourceReferences(List<ChoreographyResourceReference> results);
    }

    /// <summary>
    /// Optional capability for choreography assets that can collect preload references while enforcing caller-owned
    /// capacity and traversal budgets. Implementations must check the supplied result ceiling before each append and
    /// stop scanning before exceeding the supplied node ceiling. Returning <c>false</c> rejects the whole collection;
    /// callers may discard references appended before the rejection.
    /// </summary>
    public interface IBoundedChoreographyResourceCollector
    {
        /// <summary>
        /// Appends distinct resource references without exceeding either supplied limit. Implementations must not
        /// clear <paramref name="results"/>. <paramref name="scannedNodeCount"/> reports the logical asset or
        /// precomputed-index nodes inspected by this call.
        /// </summary>
        bool TryCollectResourceReferences(
            List<ChoreographyResourceReference> results,
            int maximumResultCount,
            int maximumNodeScanCount,
            out int addedCount,
            out int scannedNodeCount);
    }
}
