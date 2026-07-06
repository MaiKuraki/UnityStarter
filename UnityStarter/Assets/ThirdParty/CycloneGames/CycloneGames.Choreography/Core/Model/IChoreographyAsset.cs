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
}
