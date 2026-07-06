namespace CycloneGames.Choreography.Core
{
    /// <summary>
    /// Immutable authored description of a single time-bounded item on a <see cref="ChoreographyTrack"/>
    /// (an animation clip, a sound, a VFX burst, etc.). Timing values are section-relative seconds.
    /// Instances are treated as read-only playback data; the Core layer never mutates them at runtime.
    /// </summary>
    public sealed class ChoreographyClip
    {
        /// <summary>Stable identifier, unique within its owning track. Used in provider callbacks and diagnostics.</summary>
        public string Id { get; }

        /// <summary>Resource this clip drives. May be <see cref="ChoreographyResourceReference.IsValid"/> == false for pure markers.</summary>
        public ChoreographyResourceReference Resource { get; }

        /// <summary>Start offset from the owning section start, in seconds. Must be &gt;= 0.</summary>
        public double StartTime { get; }

        /// <summary>Duration in seconds. A value &lt;= 0 marks a fire-and-forget one-shot with no explicit end.</summary>
        public double Duration { get; }

        /// <summary>Authored base blend weight in [0, 1]. Strategies may rescale this at runtime.</summary>
        public float Weight { get; }

        /// <summary>Optional sub-channel within the track (e.g. an animation layer or an audio bus index).</summary>
        public int Channel { get; }

        /// <summary>Whether the clip loops for the duration of its owning section.</summary>
        public bool Loop { get; }

        public ChoreographyClip(
            string id,
            ChoreographyResourceReference resource,
            double startTime,
            double duration,
            float weight = 1f,
            int channel = 0,
            bool loop = false)
        {
            Id = id;
            Resource = resource;
            StartTime = startTime < 0d ? 0d : startTime;
            Duration = duration;
            Weight = weight < 0f ? 0f : (weight > 1f ? 1f : weight);
            Channel = channel;
            Loop = loop;
        }

        /// <summary>End time relative to the owning section, in seconds. One-shots return <see cref="StartTime"/>.</summary>
        public double EndTime => Duration > 0d ? StartTime + Duration : StartTime;

        /// <summary>True when the clip has an explicit, non-zero duration.</summary>
        public bool HasDuration => Duration > 0d;
    }
}
