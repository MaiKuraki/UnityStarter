namespace CycloneGames.DeviceFeedback.Runtime
{
    public interface IHapticFeedbackService
    {
        bool IsAvailable { get; }
        bool IsActive { get; set; }

        void Initialize();
        void PlayPreset(HapticPreset preset);

        /// <param name="normalizedIntensity">0.0 ~ 1.0</param>
        /// <param name="sharpness">0.0 (deep/broad) ~ 1.0 (sharp/crisp). Native on iOS 13+; approximated elsewhere.</param>
        /// <param name="durationSeconds">Duration in seconds.</param>
        void Play(float normalizedIntensity, float durationSeconds, float sharpness = 0.5f);

        /// <summary>
        /// Dual-curve haptic: intensity + sharpness both vary over time.
        /// Curves X-axis: normalized time 0~1. Y-axis: 0~1.
        /// </summary>
        void PlayCurve(UnityEngine.AnimationCurve intensityCurve, float durationSeconds,
                       UnityEngine.AnimationCurve sharpnessCurve = null, int sampleIntervalMs = 20);

        void PlayClip(HapticClip clip);
        void Cancel();
    }
}
