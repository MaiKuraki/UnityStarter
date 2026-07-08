using System;

namespace CycloneGames.Choreography.Core
{
    /// <summary>
    /// A single lane of a <see cref="ChoreographySection"/> holding clips of one <see cref="ChoreographyTrackKind"/>.
    /// Clips are expected to be authored in ascending <see cref="ChoreographyClip.StartTime"/> order; the runtime
    /// pipeline sorts flattened clips defensively, but authoring tools should keep tracks pre-sorted.
    /// </summary>
    public sealed class ChoreographyTrack
    {
        private static readonly ChoreographyClip[] EmptyClips = Array.Empty<ChoreographyClip>();

        /// <summary>Stable identifier, unique within its owning section.</summary>
        public string Id { get; }

        /// <summary>Content classification used to route samples to the matching provider.</summary>
        public ChoreographyTrackKind Kind { get; }

        /// <summary>Authored clips. Never null; empty when the track carries no time-bounded content.</summary>
        public ChoreographyClip[] Clips { get; }

        public ChoreographyTrack(string id, ChoreographyTrackKind kind, ChoreographyClip[] clips)
        {
            Id = id;
            Kind = kind;
            Clips = CopyOrEmpty(clips);
        }

        private static ChoreographyClip[] CopyOrEmpty(ChoreographyClip[] clips)
        {
            if (clips == null || clips.Length == 0)
            {
                return EmptyClips;
            }

            ChoreographyClip[] copy = new ChoreographyClip[clips.Length];
            Array.Copy(clips, copy, clips.Length);
            return copy;
        }
    }
}
