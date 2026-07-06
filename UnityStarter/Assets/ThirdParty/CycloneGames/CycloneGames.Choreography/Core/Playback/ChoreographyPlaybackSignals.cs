namespace CycloneGames.Choreography.Core
{
    /// <summary>
    /// Identifies the authority that produced a timeline step. The player does not special-case these values;
    /// they are carried through samples and events so providers can make synchronization decisions.
    /// </summary>
    public enum ChoreographyClockKind
    {
        ManualDelta = 0,
        FixedTick = 1,
        AudioDspTime = 2,
        Animation = 3,
        Timeline = 4,
        ExternalAbsolute = 5
    }

    /// <summary>
    /// Describes how a timeline step advances a player.
    /// </summary>
    public enum ChoreographyTimelineStepMode
    {
        Delta = 0,
        Absolute = 1
    }

    /// <summary>
    /// Engine-free time authority sample consumed by <see cref="ChoreographyPlayer"/> and
    /// <see cref="ChoreographyScheduler"/>. Delta steps advance from the current playhead. Absolute steps set the
    /// playhead from an external authoritative timeline such as an animation playable, Unity Timeline, or DSP clock.
    /// </summary>
    public readonly struct ChoreographyTimelineStep
    {
        public const long UnspecifiedTickIndex = -1;

        public readonly ChoreographyTimelineStepMode Mode;
        public readonly ChoreographyClockKind ClockKind;
        public readonly double DeltaTime;
        public readonly double TargetTime;
        public readonly double SourceTime;
        public readonly long TickIndex;
        public readonly double TickRate;

        private ChoreographyTimelineStep(
            ChoreographyTimelineStepMode mode,
            ChoreographyClockKind clockKind,
            double deltaTime,
            double targetTime,
            double sourceTime,
            long tickIndex,
            double tickRate)
        {
            Mode = mode;
            ClockKind = clockKind;
            DeltaTime = SanitizeNonNegative(deltaTime);
            TargetTime = SanitizeNonNegative(targetTime);
            SourceTime = double.IsNaN(sourceTime) || double.IsInfinity(sourceTime) ? 0d : sourceTime;
            TickIndex = tickIndex;
            TickRate = tickRate > 0d && !double.IsNaN(tickRate) && !double.IsInfinity(tickRate) ? tickRate : 0d;
        }

        public static ChoreographyTimelineStep FromDelta(
            double deltaTime,
            ChoreographyClockKind clockKind = ChoreographyClockKind.ManualDelta,
            long tickIndex = UnspecifiedTickIndex,
            double tickRate = 0d,
            double sourceTime = 0d)
        {
            return new ChoreographyTimelineStep(
                ChoreographyTimelineStepMode.Delta,
                clockKind,
                deltaTime,
                0d,
                sourceTime,
                tickIndex,
                tickRate);
        }

        public static ChoreographyTimelineStep FromAbsolute(
            double targetTime,
            ChoreographyClockKind clockKind = ChoreographyClockKind.ExternalAbsolute,
            long tickIndex = UnspecifiedTickIndex,
            double tickRate = 0d,
            double sourceTime = 0d)
        {
            return new ChoreographyTimelineStep(
                ChoreographyTimelineStepMode.Absolute,
                clockKind,
                0d,
                targetTime,
                sourceTime,
                tickIndex,
                tickRate);
        }

        private static double SanitizeNonNegative(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0d)
            {
                return 0d;
            }

            return value;
        }
    }

    /// <summary>
    /// Resolved, per-tick description of one active clip after strategy weighting. Passed by <c>in</c> to keep
    /// dispatch allocation-free. Time values are double-precision seconds so providers can align animation,
    /// audio, and events against an external clock without losing authored precision.
    /// </summary>
    public readonly struct ChoreographyPlaybackSample
    {
        /// <summary>Scheduler instance id that owns this sample. 0 for a standalone <see cref="ChoreographyPlayer"/>.</summary>
        public readonly int InstanceId;

        /// <summary>Track kind used to route the sample to the matching provider.</summary>
        public readonly ChoreographyTrackKind TrackKind;

        /// <summary>The authored clip being sampled.</summary>
        public readonly ChoreographyClip Clip;

        /// <summary>Absolute choreography timeline time for this sample, in seconds.</summary>
        public readonly double TimelineTime;

        /// <summary>Elapsed time within the clip, in seconds (wrapped when the clip loops).</summary>
        public readonly double LocalTime;

        /// <summary>Normalized time within the clip in [0, 1]. Equals 0 for one-shots.</summary>
        public readonly double NormalizedTime;

        /// <summary>Effective blend/mix weight in [0, 1] after strategy resolution.</summary>
        public readonly float Weight;

        /// <summary>Competition channel this sample belongs to. Instances on different playback channels do not compete.</summary>
        public readonly int PlaybackChannel;

        /// <summary>Provider-specific clip sub-channel, such as an animation layer or audio bus index.</summary>
        public readonly int ClipChannel;

        /// <summary>Clock authority that produced this sample.</summary>
        public readonly ChoreographyClockKind ClockKind;

        /// <summary>Tick/frame/sample index from the authority, or <see cref="ChoreographyTimelineStep.UnspecifiedTickIndex"/>.</summary>
        public readonly long TickIndex;

        /// <summary>Authority source time, such as Unity DSP time, when available.</summary>
        public readonly double SourceTime;

        /// <summary>Competition channel this sample belongs to. Kept for source compatibility.</summary>
        public int Channel => PlaybackChannel;

        public ChoreographyPlaybackSample(
            int instanceId,
            ChoreographyTrackKind trackKind,
            ChoreographyClip clip,
            double timelineTime,
            double localTime,
            double normalizedTime,
            float weight,
            int playbackChannel,
            int clipChannel = 0,
            ChoreographyClockKind clockKind = ChoreographyClockKind.ManualDelta,
            long tickIndex = ChoreographyTimelineStep.UnspecifiedTickIndex,
            double sourceTime = 0d)
        {
            InstanceId = instanceId;
            TrackKind = trackKind;
            Clip = clip;
            TimelineTime = timelineTime;
            LocalTime = localTime;
            NormalizedTime = normalizedTime;
            Weight = weight;
            PlaybackChannel = playbackChannel;
            ClipChannel = clipChannel;
            ClockKind = clockKind;
            TickIndex = tickIndex;
            SourceTime = sourceTime;
        }
    }

    /// <summary>
    /// Describes a clip that has stopped. <see cref="Completed"/> distinguishes natural completion from
    /// interruption so providers can choose to fade out versus hard-stop.
    /// </summary>
    public readonly struct ChoreographyClipStop
    {
        public readonly int InstanceId;
        public readonly ChoreographyTrackKind TrackKind;
        public readonly string ClipId;
        public readonly int PlaybackChannel;
        public readonly int ClipChannel;
        public readonly bool Completed;
        public readonly double TimelineTime;
        public readonly ChoreographyClockKind ClockKind;
        public readonly long TickIndex;

        /// <summary>Competition channel this stopped clip belongs to. Kept for source compatibility.</summary>
        public int Channel => PlaybackChannel;

        public ChoreographyClipStop(
            int instanceId,
            ChoreographyTrackKind trackKind,
            string clipId,
            int playbackChannel,
            bool completed,
            int clipChannel = 0,
            double timelineTime = 0d,
            ChoreographyClockKind clockKind = ChoreographyClockKind.ManualDelta,
            long tickIndex = ChoreographyTimelineStep.UnspecifiedTickIndex)
        {
            InstanceId = instanceId;
            TrackKind = trackKind;
            ClipId = clipId;
            PlaybackChannel = playbackChannel;
            ClipChannel = clipChannel;
            Completed = completed;
            TimelineTime = timelineTime;
            ClockKind = clockKind;
            TickIndex = tickIndex;
        }
    }

    /// <summary>
    /// A dispatched timeline event with the originating instance context.
    /// </summary>
    public readonly struct ChoreographyEventInvocation
    {
        public readonly int InstanceId;
        public readonly ChoreographyEvent Event;
        public readonly double ScheduledTime;
        public readonly double DispatchTime;
        public readonly double DispatchDelay;
        public readonly ChoreographyClockKind ClockKind;
        public readonly long TickIndex;

        public ChoreographyEventInvocation(
            int instanceId,
            in ChoreographyEvent choreographyEvent,
            double scheduledTime = 0d,
            double dispatchTime = 0d,
            ChoreographyClockKind clockKind = ChoreographyClockKind.ManualDelta,
            long tickIndex = ChoreographyTimelineStep.UnspecifiedTickIndex)
        {
            InstanceId = instanceId;
            Event = choreographyEvent;
            ScheduledTime = scheduledTime;
            DispatchTime = dispatchTime;
            DispatchDelay = dispatchTime > scheduledTime ? dispatchTime - scheduledTime : 0d;
            ClockKind = clockKind;
            TickIndex = tickIndex;
        }
    }

    /// <summary>
    /// Callback surface a <see cref="ChoreographyPlayer"/> drives during <see cref="ChoreographyPlayer.Tick"/>.
    /// The scheduler implements this to buffer samples for cross-instance strategy resolution; a standalone
    /// player can be pointed at a direct provider dispatcher. All methods run on the tick thread and must not block.
    /// </summary>
    public interface IChoreographyPlaybackSink
    {
        void OnClipStarted(in ChoreographyPlaybackSample sample);

        void OnClipUpdated(in ChoreographyPlaybackSample sample);

        void OnClipStopped(in ChoreographyClipStop stop);

        void OnEvent(in ChoreographyEventInvocation invocation);

        void OnPlaybackCompleted(int instanceId);
    }
}
