using System;

namespace CycloneGames.AnimationRuntime
{
    /// <summary>Weight interpolation curve used by <see cref="AnimationFade"/>.</summary>
    public enum AnimationFadeEasing : byte
    {
        Linear = 0,
        SmoothStep = 1,
        EaseIn = 2,
        EaseOut = 3
    }

    /// <summary>Why a playback transition completed.</summary>
    public enum AnimationTransitionReason : byte
    {
        /// <summary>The fade reached its target weight.</summary>
        FadeCompleted = 0,

        /// <summary>The playback crossed its configured exit time.</summary>
        ExitTime = 1
    }

    /// <summary>
    /// A weight ramp for one playback. <see cref="Duration"/> is in seconds and is advanced by
    /// <see cref="AnimationTick.TransitionDelta"/>; a duration of zero applies the target weight immediately and produces
    /// no completion event.
    /// </summary>
    public readonly struct AnimationFade
    {
        public readonly float Duration;
        public readonly float TargetWeight;
        public readonly AnimationFadeEasing Easing;

        public AnimationFade(float duration, float targetWeight, AnimationFadeEasing easing = AnimationFadeEasing.Linear)
        {
            Duration = float.IsNaN(duration) || float.IsInfinity(duration) || duration < 0f ? 0f : duration;
            TargetWeight = float.IsNaN(targetWeight) || targetWeight <= 0f ? 0f : (targetWeight >= 1f ? 1f : targetWeight);
            Easing = easing <= AnimationFadeEasing.EaseOut ? easing : AnimationFadeEasing.Linear;
        }

        public bool IsInstant => Duration <= 0f;

        /// <summary>Evaluates the easing curve at a normalized time in [0,1]. Public so tests can use the same curve.</summary>
        public static float Evaluate(AnimationFadeEasing easing, float t)
        {
            if (t <= 0f)
            {
                return 0f;
            }

            if (t >= 1f)
            {
                return 1f;
            }

            switch (easing)
            {
                case AnimationFadeEasing.SmoothStep:
                    return t * t * (3f - (2f * t));
                case AnimationFadeEasing.EaseIn:
                    return t * t;
                case AnimationFadeEasing.EaseOut:
                {
                    float inverse = 1f - t;
                    return 1f - (inverse * inverse);
                }
                default:
                    return t;
            }
        }
    }
}
