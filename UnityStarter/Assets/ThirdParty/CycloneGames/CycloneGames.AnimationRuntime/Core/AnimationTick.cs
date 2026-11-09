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
    /// How the runtime maps commit ticks onto clip time. The policy is fixed at graph construction so the hot
    /// path never branches on a mutable flag, and so a host cannot end up with two time authorities by accident.
    /// </summary>
    public enum AnimationTimePolicy : byte
    {
        /// <summary>
        /// The host owns clip time. <c>BeginClip</c>/<c>UpdateClip</c> carry absolute local seconds and
        /// <see cref="AnimationTick.Time"/> is diagnostic only. This is the contract a timeline or replay system wants.
        /// </summary>
        External = 0,

        /// <summary>
        /// The runtime derives clip time from consecutive tick times, using the start clock captured when a playback
        /// began. <c>UpdateClip</c> then only changes weight/speed; use <c>SeekClip</c> for an explicit seek.
        /// This is the contract a self-driven actor wants.
        /// </summary>
        TickDriven = 1
    }

    /// <summary>Wrap behaviour applied to clip time when the graph runs <see cref="AnimationTimePolicy.TickDriven"/>.</summary>
    public enum AnimationLoopMode : byte
    {
        /// <summary>Stop at the clip bounds.</summary>
        Clamp = 0,

        /// <summary>Wrap around the clip length.</summary>
        Loop = 1,

        /// <summary>Reverse direction at the clip bounds.</summary>
        PingPong = 2
    }

    /// <summary>
    /// One commit tick. <see cref="StepIndex"/> always drives commit idempotency.
    ///
    /// <see cref="Time"/> is the clip clock: authoritative only when the graph was constructed with
    /// <see cref="AnimationTimePolicy.TickDriven"/>. Under <see cref="AnimationTimePolicy.External"/> it is a
    /// diagnostic value, because the host already supplied absolute local time through its clip requests.
    ///
    /// <see cref="Delta"/> is the transition clock and is always authoritative: it advances weight fades and
    /// exit timers regardless of the clip time policy.
    /// </summary>
    public readonly struct AnimationTick
    {
        /// <summary>Sentinel that never matches a real step.</summary>
        public static readonly AnimationTick None = new AnimationTick(0d, -1L, AnimationTimeSource.Update);

        /// <summary>Clip clock reading for this commit. Authoritative only under TickDriven.</summary>
        public readonly double Time;

        /// <summary>Monotonic step index. Two commits with the same index evaluate once.</summary>
        public readonly long StepIndex;

        /// <summary>Whether the host clock moved backwards (scrub, rewind). Tick-driven graphs re-base their clocks.</summary>
        public readonly bool IsBackward;

        /// <summary>Where <see cref="Time"/> came from. Diagnostics only.</summary>
        public readonly AnimationTimeSource Source;

        /// <summary>Transition delta in seconds, clamped to a non-negative finite value. Advances fades and exit timers.</summary>
        public readonly float Delta;

        public AnimationTick(
            double time,
            long stepIndex,
            AnimationTimeSource source,
            bool isBackward = false,
            float delta = 0f)
        {
            Time = Sanitize(time);
            StepIndex = stepIndex;
            Source = source;
            IsBackward = isBackward;
            Delta = SanitizeDelta(delta);
        }

        /// <summary>Whether this tick carries a usable step index. Index 0 is a valid first step.</summary>
        public bool IsValid => StepIndex >= 0;

        /// <summary>Convenience factory for host adapters that drive a single phase.</summary>
        public static AnimationTick Create(double time, long stepIndex, AnimationTimeSource source, float deltaTime)
        {
            return new AnimationTick(time, stepIndex, source, false, deltaTime);
        }

        private static double Sanitize(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value) ? 0d : value;
        }

        private static float SanitizeDelta(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) || value < 0f ? 0f : value;
        }

        public override string ToString()
        {
            return "Tick(t=" + Time + ", step=" + StepIndex + ", " + Source + (IsBackward ? ", backward" : string.Empty) + ")";
        }
    }
}
