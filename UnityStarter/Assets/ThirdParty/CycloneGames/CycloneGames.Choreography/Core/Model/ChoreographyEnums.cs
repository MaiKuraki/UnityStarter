namespace CycloneGames.Choreography.Core
{
    /// <summary>
    /// Classifies the kind of content carried by a <see cref="ChoreographyTrack"/>.
    /// Providers are dispatched by track kind, so this value is part of the authored data contract.
    /// </summary>
    public enum ChoreographyTrackKind : byte
    {
        Animation = 0,
        Audio = 1,
        Vfx = 2,
        Event = 3,
        Custom = 4
    }

    /// <summary>
    /// Classifies the kind of resource referenced by a clip. Used by preload and provider routing.
    /// </summary>
    public enum ChoreographyResourceKind : byte
    {
        Generic = 0,
        Animation = 1,
        AudioClip = 2,
        AudioEvent = 3,
        AudioBank = 4,
        Vfx = 5,
        ExternalBank = 6,
        WwiseEvent = 7,
        BackendCue = 8
    }

    /// <summary>
    /// Playback strategy hint used when multiple choreographies compete on the same channel.
    /// Concrete behavior is defined by the matching <see cref="IPlaybackStrategy"/> implementation.
    /// </summary>
    public enum ChoreographyPlaybackMode : byte
    {
        Priority = 0,
        Blend = 1,
        Override = 2,
        Additive = 3,
        Queue = 4,

        /// <summary>Use the owning play request's default mode for this section.</summary>
        Inherit = 255
    }

    /// <summary>
    /// Lifecycle phase of a duration-spanning event state.
    /// </summary>
    public enum EventStatePhase : byte
    {
        Begin = 0,
        Update = 1,
        End = 2
    }

    /// <summary>
    /// Lifecycle status of a single <see cref="ChoreographyPlayer"/> instance.
    /// </summary>
    public enum PlaybackStatus : byte
    {
        Idle = 0,
        Playing = 1,
        Paused = 2,
        Completed = 3,
        Stopped = 4
    }

    /// <summary>
    /// Result of a strategy admission decision when a new play request competes on a channel.
    /// </summary>
    public enum ChoreographyAdmission : byte
    {
        /// <summary>Start the incoming request alongside existing instances.</summary>
        Admit = 0,
        /// <summary>Reject the incoming request; it never starts.</summary>
        Reject = 1,
        /// <summary>Defer the incoming request until the channel becomes free.</summary>
        Queue = 2,
        /// <summary>Stop existing instances on the channel and start the incoming request.</summary>
        Replace = 3
    }

    /// <summary>
    /// Failure handling policy for <see cref="PreloadRunner"/>.
    /// </summary>
    public enum PreloadFailurePolicy : byte
    {
        /// <summary>Keep loading remaining references after a failure and report failures in the result.</summary>
        Continue = 0,
        /// <summary>Abort the remaining loads on the first failure.</summary>
        Abort = 1
    }

    /// <summary>
    /// Status of a <see cref="PreloadRunner"/> batch.
    /// </summary>
    public enum PreloadStatus : byte
    {
        Idle = 0,
        Loading = 1,
        Completed = 2,
        Failed = 3,
        Cancelled = 4
    }
}
