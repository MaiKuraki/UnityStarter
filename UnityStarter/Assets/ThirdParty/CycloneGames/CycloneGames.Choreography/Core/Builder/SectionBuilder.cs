using System;
using System.Collections.Generic;

namespace CycloneGames.Choreography.Core
{
    public sealed class SectionBuilder
    {
        private readonly string _id;
        private readonly double _duration;
        private readonly List<TrackBuilder> _tracks = new List<TrackBuilder>(4);
        private readonly List<ChoreographyEvent> _events = new List<ChoreographyEvent>(4);
        private readonly List<ChoreographyEventState> _eventStates = new List<ChoreographyEventState>(2);
        private bool _interruptible = true;
        private ChoreographyPlaybackMode _preferredMode = ChoreographyPlaybackMode.Inherit;
        private ChoreographySectionClock _clock = ChoreographySectionClock.Default;

        public SectionBuilder(string id, double duration)
        {
            _id = id ?? string.Empty;
            _duration = duration;
        }

        public SectionBuilder Interruptible(bool value)
        {
            _interruptible = value;
            return this;
        }

        public SectionBuilder PreferredMode(ChoreographyPlaybackMode mode)
        {
            _preferredMode = mode;
            return this;
        }

        public SectionBuilder Clock(
            ChoreographySectionClockSource source,
            ChoreographyExternalClockEndPolicy endPolicy = ChoreographyExternalClockEndPolicy.ContinueInternal,
            double frameRate = 60d)
        {
            _clock = new ChoreographySectionClock(source, endPolicy, frameRate);
            return this;
        }

        public TrackBuilder Track(string id, ChoreographyTrackKind kind)
        {
            TrackBuilder track = new TrackBuilder(id, kind);
            _tracks.Add(track);
            return track;
        }

        public SectionBuilder Track(string id, ChoreographyTrackKind kind, Action<TrackBuilder> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            TrackBuilder track = Track(id, kind);
            configure(track);
            return this;
        }

        public SectionBuilder Event(string eventId, double time, float magnitude = 1f, int intPayload = 0, string stringPayload = null)
        {
            _events.Add(new ChoreographyEvent(eventId, time, magnitude, intPayload, stringPayload));
            return this;
        }

        public SectionBuilder EventState(
            string id,
            string eventId,
            double startTime,
            double endTime,
            float magnitude = 1f,
            int intPayload = 0,
            string stringPayload = null)
        {
            _eventStates.Add(new ChoreographyEventState(id, eventId, startTime, endTime, magnitude, intPayload, stringPayload));
            return this;
        }

        internal ChoreographySection Build()
        {
            ChoreographyTrack[] tracks = new ChoreographyTrack[_tracks.Count];
            for (int i = 0; i < _tracks.Count; i++)
            {
                tracks[i] = _tracks[i].Build();
            }

            return new ChoreographySection(
                _id,
                _duration,
                tracks,
                _events.Count > 0 ? _events.ToArray() : null,
                _interruptible,
                _preferredMode,
                _eventStates.Count > 0 ? _eventStates.ToArray() : null,
                _clock);
        }
    }
}
