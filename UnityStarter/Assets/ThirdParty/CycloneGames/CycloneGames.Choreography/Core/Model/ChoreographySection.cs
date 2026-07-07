using System;

namespace CycloneGames.Choreography.Core
{
    /// <summary>
    /// A sequential segment of a choreography (e.g. Windup, Active, Recovery). Sections play back-to-back
    /// along the asset timeline. Each section owns its tracks and events and declares how it competes with
    /// other choreographies via <see cref="PreferredMode"/> and <see cref="Interruptible"/>.
    /// </summary>
    public sealed class ChoreographySection
    {
        private static readonly ChoreographyTrack[] EmptyTracks = Array.Empty<ChoreographyTrack>();
        private static readonly ChoreographyEvent[] EmptyEvents = Array.Empty<ChoreographyEvent>();
        private static readonly ChoreographyEventState[] EmptyEventStates = Array.Empty<ChoreographyEventState>();

        /// <summary>Stable identifier, unique within its owning asset.</summary>
        public string Id { get; }

        /// <summary>Section length in seconds. Must be &gt; 0 for the timeline to advance past it.</summary>
        public double Duration { get; }

        /// <summary>
        /// When false, the scheduler must not interrupt this section (e.g. a committed attack windup).
        /// Strategies consult this flag before replacing an active instance.
        /// </summary>
        public bool Interruptible { get; }

        /// <summary>Default competition strategy applied while this section is the active dominant section on a channel.</summary>
        public ChoreographyPlaybackMode PreferredMode { get; }

        /// <summary>Preferred time authority for this section. The active clock driver decides whether it can satisfy it.</summary>
        public ChoreographySectionClock Clock { get; }

        /// <summary>Tracks owned by this section. Never null.</summary>
        public ChoreographyTrack[] Tracks { get; }

        /// <summary>Events owned by this section, expected in ascending <see cref="ChoreographyEvent.Time"/> order. Never null.</summary>
        public ChoreographyEvent[] Events { get; }

        /// <summary>Duration-spanning event states owned by this section. Never null.</summary>
        public ChoreographyEventState[] EventStates { get; }

        public ChoreographySection(
            string id,
            double duration,
            ChoreographyTrack[] tracks,
            ChoreographyEvent[] events = null,
            bool interruptible = true,
            ChoreographyPlaybackMode preferredMode = ChoreographyPlaybackMode.Inherit,
            ChoreographyEventState[] eventStates = null,
            ChoreographySectionClock clock = default)
        {
            Id = id;
            Duration = duration < 0d ? 0d : duration;
            Tracks = tracks ?? EmptyTracks;
            Events = events ?? EmptyEvents;
            Interruptible = interruptible;
            PreferredMode = preferredMode;
            EventStates = eventStates ?? EmptyEventStates;
            Clock = clock.Source == ChoreographySectionClockSource.Inherit
                && clock.ExternalEndPolicy == 0
                && clock.FrameRate == 0d
                    ? ChoreographySectionClock.Default
                    : clock;
        }
    }
}
