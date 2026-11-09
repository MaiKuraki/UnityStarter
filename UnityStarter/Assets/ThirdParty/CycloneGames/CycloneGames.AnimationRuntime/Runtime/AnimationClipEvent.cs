using CycloneGames.AnimationRuntime.Core;

namespace CycloneGames.AnimationRuntime
{
    /// <summary>
    /// One clip-local marker, converted from <see cref="UnityEngine.AnimationClip.events"/> at first use.
    /// Strings are borrowed from the clip; the runtime never copies them.
    /// </summary>
    public readonly struct AnimationClipEvent
    {
        public readonly int EventId;
        public readonly float NormalizedTime;
        public readonly string FunctionName;
        public readonly string StringParameter;
        public readonly int IntParameter;
        public readonly float FloatParameter;

        public AnimationClipEvent(
            int eventId,
            float normalizedTime,
            string functionName,
            string stringParameter,
            int intParameter,
            float floatParameter)
        {
            EventId = eventId;
            NormalizedTime = normalizedTime;
            FunctionName = functionName;
            StringParameter = stringParameter;
            IntParameter = intParameter;
            FloatParameter = floatParameter;
        }
    }

    /// <summary>
    /// Receives clip-local events. Registered once per graph; called on the main thread after the pose has been
    /// evaluated. Implementations must not call back into <c>Commit</c>.
    /// </summary>
    public interface IAnimationClipEventSink
    {
        void OnClipEvent(in AnimationPlaybackHandle handle, in AnimationClipEvent clipEvent);
    }

    /// <summary>When an exit time fires.</summary>
    public enum AnimationExitMode : byte
    {
        /// <summary>Once per playback, at the first crossing.</summary>
        Once = 0,

        /// <summary>Every time the normalized position crosses the target, including loop wraps.</summary>
        EveryLoop = 1
    }

    /// <summary>
    /// Normalized exit point for one playback. Fires <see cref="AnimationRuntimeGraph.TransitionCompleted"/> with
    /// <see cref="AnimationTransitionReason.ExitTime"/>; it does not stop the playback by itself, so the host decides
    /// whether to fade out, hold or release.
    /// </summary>
    public readonly struct AnimationExitTime
    {
        public readonly float NormalizedTime;
        public readonly AnimationExitMode Mode;

        public AnimationExitTime(float normalizedTime, AnimationExitMode mode = AnimationExitMode.Once)
        {
            NormalizedTime = float.IsNaN(normalizedTime) || normalizedTime < 0f
                ? 0f
                : (normalizedTime > 1f ? 1f : normalizedTime);
            Mode = mode == AnimationExitMode.EveryLoop ? AnimationExitMode.EveryLoop : AnimationExitMode.Once;
        }
    }
}
