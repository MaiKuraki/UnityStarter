using UnityEngine;

namespace CycloneGames.DeviceFeedback.Runtime
{
    /// <summary>
    /// Static facade for non-DI usage, scoped to the mobile device's built-in vibration hardware.
    /// For DI, register <see cref="MobileVibrationService"/> as <see cref="IMobileVibrationService"/>
    /// or <see cref="IHapticFeedbackService"/> for device-agnostic haptics.
    /// </summary>
    public static class MobileVibration
    {
        private static volatile MobileVibrationService s_instance;
        private static readonly object s_lock = new object();

        private static MobileVibrationService Instance
        {
            get
            {
                if (s_instance != null) return s_instance;

                lock (s_lock)
                {
                    if (s_instance == null)
                    {
                        var svc = new MobileVibrationService();
                        svc.Initialize();
                        s_instance = svc;
                    }
                }
                return s_instance;
            }
        }

        public static void Init() => _ = Instance;

        public static bool IsAvailable => Instance.IsAvailable;
        public static bool HasVibrator => Instance.HasVibrator;

        public static void SetActive(bool active)
        {
            Instance.IsActive = active;
            if (!active) Instance.Cancel();
        }

        // ── Common haptic interface (IHapticFeedbackService) ──

        public static void PlayPreset(HapticPreset preset) => Instance.PlayPreset(preset);
        public static void Play(float normalizedIntensity, float durationSeconds, float sharpness = 0.5f) => Instance.Play(normalizedIntensity, durationSeconds, sharpness);
        public static void PlayCurve(AnimationCurve intensityCurve, float durationSeconds, AnimationCurve sharpnessCurve = null, int sampleIntervalMs = 20) => Instance.PlayCurve(intensityCurve, durationSeconds, sharpnessCurve, sampleIntervalMs);
        public static void PlayClip(HapticClip clip) => Instance.PlayClip(clip);
        public static void Cancel() => Instance.Cancel();

        // ── Mobile-specific ──

        public static void Vibrate() => Instance.Vibrate();
        public static void Vibrate(long milliseconds) => Instance.Vibrate(milliseconds);
        public static void Vibrate(long[] pattern, int repeat = -1) => Instance.Vibrate(pattern, repeat);
        public static void VibratePop() => Instance.VibratePop();
        public static void VibratePeek() => Instance.VibratePeek();
        public static void VibrateNope() => Instance.VibrateNope();

        // ── iOS-specific ──

        public static void VibrateIOS(IOSImpactStyle style) => Instance.VibrateIOS(style);
        public static void VibrateIOS(IOSNotificationStyle style) => Instance.VibrateIOS(style);
        public static void VibrateIOSSelection() => Instance.VibrateIOSSelection();
        public static void UpdateContinuousParameters(float intensity, float sharpness) => Instance.UpdateContinuousParameters(intensity, sharpness);

        // ── Platform info ──

        private static int? s_androidSdkVersion;

        public static int AndroidSdkVersion
        {
            get
            {
                if (s_androidSdkVersion.HasValue) return s_androidSdkVersion.Value;

#if UNITY_ANDROID
                if (Application.platform == RuntimePlatform.Android)
                {
                    using (var build = new AndroidJavaClass("android.os.Build$VERSION"))
                    {
                        s_androidSdkVersion = build.GetStatic<int>("SDK_INT");
                    }
                }
                else
#endif
                {
                    s_androidSdkVersion = 0;
                }
                return s_androidSdkVersion.Value;
            }
        }
    }
}
