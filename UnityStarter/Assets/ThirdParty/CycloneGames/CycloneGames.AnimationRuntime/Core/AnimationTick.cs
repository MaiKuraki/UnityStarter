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
    /// One commit tick. Time is absolute; the runtime never accumulates a delta.
    /// <see cref="DiagnosticDelta"/> is for tooling only and must not be used to advance a clip.
    /// </summary>
    public readonly struct AnimationTick
    {
        /// <summary>Sentinel that never matches a real step.</summary>
        public static readonly AnimationTick None = new AnimationTick(0d, -1L, AnimationTimeSource.Update);

        public readonly double Time;
        public readonly long StepIndex;
        public readonly bool IsBackward;
        public readonly AnimationTimeSource Source;
        public readonly double SourceTime;
        public readonly float DiagnosticDelta;

        public AnimationTick(
            double time,
            long stepIndex,
            AnimationTimeSource source,
            bool isBackward = false,
            double sourceTime = 0d,
            float diagnosticDelta = 0f)
        {
            Time = double.IsNaN(time) || double.IsInfinity(time) ? 0d : time;
            StepIndex = stepIndex;
            Source = source;
            IsBackward = isBackward;
            SourceTime = double.IsNaN(sourceTime) || double.IsInfinity(sourceTime) ? 0d : sourceTime;
            DiagnosticDelta = diagnosticDelta < 0f ? 0f : diagnosticDelta;
        }

        /// <summary>Whether this tick carries a usable step index. Index 0 is a valid first step.</summary>
        public bool IsValid => StepIndex >= 0;

        /// <summary>Convenience factory for host adapters that drive a single phase.</summary>
        public static AnimationTick Create(double time, long stepIndex, AnimationTimeSource source, float deltaTime)
        {
            return new AnimationTick(time, stepIndex, source, false, 0d, deltaTime);
        }

        public override string ToString()
        {
            return "Tick(t=" + Time + ", step=" + StepIndex + ", " + Source + (IsBackward ? ", backward" : string.Empty) + ")";
        }
    }
}
