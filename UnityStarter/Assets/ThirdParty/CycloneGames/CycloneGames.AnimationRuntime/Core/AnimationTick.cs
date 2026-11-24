using System;

namespace CycloneGames.AnimationRuntime.Core
{
    /// <summary>Where the authoritative time of a commit came from. Diagnostics and replay only.</summary>
    public enum AnimationTimeSource : byte
    {
        Update = 0,
        LateUpdate = 1,
        FixedUpdate = 2,
        External = 3,
        Replay = 4,
        Editor = 5
    }

    /// <summary>
    /// How the runtime maps commit ticks onto clip time. Both policies consume the same absolute clock;
    /// the difference is only who supplies the clip-local seconds. Neither policy accumulates anything.
    /// </summary>
    public enum AnimationTimePolicy : byte
    {
        /// <summary>The host supplies absolute clip-local seconds on every request. Tick.ClipTime is the global authority.</summary>
        External = 0,

        /// <summary>The graph folds the global Tick.ClipTime through the request's speed and loop mode.</summary>
        TickDriven = 1
    }

    /// <summary>Wrap behaviour applied to the absolute clock when the graph folds it.</summary>
    public enum AnimationLoopMode : byte
    {
        Clamp = 0,
        Loop = 1,
        PingPong = 2
    }

    /// <summary>
    /// One commit frame. All clocks are absolute; the runtime never accumulates a delta, so a pose and its
    /// weights are a pure function of (ClipTime, TransitionTime, StepIndex). Replaying the same tick
    /// sequence reproduces the same state, and rewinding to an earlier tick reproduces the earlier weights.
    /// </summary>
    public readonly struct AnimationTick
    {
        /// <summary>Sentinel that never matches a real step.</summary>
        public static AnimationTick None => new AnimationTick(0d, 0d, -1L, AnimationTimeSource.Update);

        /// <summary>Global authoritative clock in seconds. Feeds clip folding and the event sweep.</summary>
        public readonly double ClipTime;

        /// <summary>Global authoritative clock in seconds for weight ramps, layer fades and exit timing.</summary>
        public readonly double TransitionTime;

        /// <summary>Monotonic step index. Two commits with the same index evaluate once.</summary>
        public readonly long StepIndex;

        /// <summary>Where the clock came from. Diagnostics only.</summary>
        public readonly AnimationTimeSource Source;

        /// <summary>Whether the host clock moved backwards (scrub, rewind).</summary>
        public readonly bool IsBackward;

        /// <summary>
        /// When set, this commit still advances pose and weights but dispatches no clip or transition
        /// events. Used by rollback re-simulation so audio, VFX and gameplay callbacks do not re-fire.
        /// </summary>
        public readonly bool SuppressEvents;

        /// <summary>Convenience for hosts whose clip clock and transition clock are the same.</summary>
        public AnimationTick(double clipTime, long stepIndex, AnimationTimeSource source,
            bool isBackward = false, bool suppressEvents = false)
            : this(clipTime, clipTime, stepIndex, source, isBackward, suppressEvents)
        {
        }

        public AnimationTick(double clipTime, double transitionTime, long stepIndex,
            AnimationTimeSource source, bool isBackward = false, bool suppressEvents = false)
        {
            ClipTime = Sanitize(clipTime);
            TransitionTime = Sanitize(transitionTime);
            StepIndex = stepIndex;
            Source = source;
            IsBackward = isBackward;
            SuppressEvents = suppressEvents;
        }

        /// <summary>Index 0 is a valid first step.</summary>
        public bool IsValid => StepIndex >= 0;

        private static double Sanitize(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value) ? 0d : value;
        }

        public override string ToString()
        {
            return "Tick(clip=" + ClipTime + ", transition=" + TransitionTime
                + ", step=" + StepIndex + ", " + Source
                + (IsBackward ? ", backward" : string.Empty)
                + (SuppressEvents ? ", suppressed" : string.Empty) + ")";
        }
    }
}
