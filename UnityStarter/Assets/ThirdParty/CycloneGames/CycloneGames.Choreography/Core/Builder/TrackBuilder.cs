using System.Collections.Generic;

namespace CycloneGames.Choreography.Core
{
    public sealed class TrackBuilder
    {
        private readonly string _id;
        private readonly ChoreographyTrackKind _kind;
        private readonly List<ChoreographyClip> _clips = new List<ChoreographyClip>(4);

        public TrackBuilder(string id, ChoreographyTrackKind kind)
        {
            _id = id ?? string.Empty;
            _kind = kind;
        }

        public TrackBuilder Clip(
            string id,
            ChoreographyResourceReference resource,
            double startTime,
            double duration,
            float weight = 1f,
            int channel = 0,
            bool loop = false)
        {
            _clips.Add(new ChoreographyClip(id, resource, startTime, duration, weight, channel, loop));
            return this;
        }

        internal ChoreographyTrack Build()
        {
            return new ChoreographyTrack(_id, _kind, _clips.Count > 0 ? _clips.ToArray() : null);
        }
    }
}
