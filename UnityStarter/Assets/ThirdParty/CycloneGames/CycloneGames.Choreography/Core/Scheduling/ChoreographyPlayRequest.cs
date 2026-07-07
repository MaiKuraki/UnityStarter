namespace CycloneGames.Choreography.Core
{
    /// <summary>
    /// Immutable per-instance request passed to <see cref="ChoreographyScheduler.Play"/>. Describes how the new
    /// choreography should compete for its channel and how it should be advanced.
    /// </summary>
    public readonly struct ChoreographyPlayRequest
    {
        /// <summary>Competition channel (e.g. an animation body part or an audio bus). Instances on different channels never compete.</summary>
        public readonly int Channel;

        /// <summary>Relative priority; higher wins under priority/override strategies.</summary>
        public readonly int Priority;

        /// <summary>Strategy selector for this request's channel while it is dominant.</summary>
        public readonly ChoreographyPlaybackMode Mode;

        /// <summary>Playback rate multiplier. Must be &gt; 0; non-positive values are treated as 1.</summary>
        public readonly double Speed;

        /// <summary>Whether the choreography loops instead of completing.</summary>
        public readonly bool Loop;

        /// <summary>
        /// Optional per-instance clock driver. Null uses the scheduler's default section-aware internal/fixed-frame driver.
        /// </summary>
        public readonly IChoreographyClockDriver ClockDriver;

        public ChoreographyPlayRequest(
            int channel = 0,
            int priority = 0,
            ChoreographyPlaybackMode mode = ChoreographyPlaybackMode.Priority,
            double speed = 1d,
            bool loop = false,
            IChoreographyClockDriver clockDriver = null)
        {
            Channel = channel;
            Priority = priority;
            Mode = mode == ChoreographyPlaybackMode.Inherit ? ChoreographyPlaybackMode.Priority : mode;
            Speed = speed <= 0d ? 1d : speed;
            Loop = loop;
            ClockDriver = clockDriver;
        }
    }
}
