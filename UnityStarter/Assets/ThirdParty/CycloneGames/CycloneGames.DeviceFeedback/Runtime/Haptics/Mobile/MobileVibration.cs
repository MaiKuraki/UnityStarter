using UnityEngine;

namespace CycloneGames.DeviceFeedback.Runtime
{
    /// <summary>
    /// Static entry point for non-DI hosts. In a composition root, inject
    /// MobileVibrationService as IMobileVibrationService or
    /// IFeedbackResultProvider instead of using this facade in the same scope.
    /// Instance access and Shutdown share one lock; the instance is cleared on
    /// subsystem registration for domain-reload-disabled play sessions.
    /// </summary>
    public static class MobileVibration
    {
        private static readonly object s_lock = new object();
        private static MobileVibrationService s_instance;
        private static int? s_androidSdkVersion;

        private static MobileVibrationService Instance
        {
            get
            {
                lock (s_lock)
                {
                    if (s_instance == null)
                    {
                        var service = new MobileVibrationService();
                        service.Initialize();
                        s_instance = service;
                    }
                    return s_instance;
                }
            }
        }

        public static void Init()
        {
            _ = Instance;
        }

        /// <summary>
        /// Shuts down and releases all native resources. Subsequent calls
        /// construct a fresh instance. Deterministic and idempotent.
        /// </summary>
        public static void Shutdown()
        {
            lock (s_lock)
            {
                if (s_instance == null) return;
                s_instance.Dispose();
                s_instance = null;
                s_androidSdkVersion = null;
            }
        }

        /// <summary>
        /// Clears the facade before a new play session when domain reload is disabled.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForSubsystemRegistration()
        {
            Shutdown();
            DeviceFeedbackDiagnostics.Reset();
        }

        public static bool IsAvailable => Instance.IsAvailable;
        public static bool HasVibrator => Instance.HasVibrator;
        public static DeviceCapability Capability => Instance.Capability;
        public static FeedbackResult LastResult => Instance.LastResult;
        public static DeviceFeedbackMemoryStats GetMemoryStats() => Instance.GetMemoryStats();

        public static void SetActive(bool active) => Instance.IsActive = active;
        public static void RefreshCapabilities() => Instance.RefreshCapabilities();
        public static void Reinitialize() => Instance.Reinitialize();

        public static void PlayPreset(HapticPreset preset) => Instance.PlayPreset(preset);
        public static void Play(float normalizedIntensity, float durationSeconds, float sharpness = 0.5f)
            => Instance.Play(normalizedIntensity, durationSeconds, sharpness);
        public static void PlayCurve(AnimationCurve intensityCurve, float durationSeconds,
                                     AnimationCurve sharpnessCurve = null, int sampleIntervalMs = 20)
            => Instance.PlayCurve(intensityCurve, durationSeconds, sharpnessCurve, sampleIntervalMs);
        public static void PlayClip(HapticClip clip) => Instance.PlayClip(clip);
        public static void Cancel() => Instance.Cancel();

        public static void Vibrate() => Instance.Vibrate();
        public static void Vibrate(long milliseconds) => Instance.Vibrate(milliseconds);
        public static void Vibrate(long[] pattern, int repeat = -1) => Instance.Vibrate(pattern, repeat);
        public static void VibratePop() => Instance.VibratePop();
        public static void VibratePeek() => Instance.VibratePeek();
        public static void VibrateNope() => Instance.VibrateNope();

        public static void VibrateIOS(IOSImpactStyle style) => Instance.VibrateIOS(style);
        public static void VibrateIOS(IOSNotificationStyle style) => Instance.VibrateIOS(style);
        public static void VibrateIOSSelection() => Instance.VibrateIOSSelection();
        public static void UpdateContinuousParameters(float intensity, float sharpness)
            => Instance.UpdateContinuousParameters(intensity, sharpness);

        public static int AndroidSdkVersion
        {
            get
            {
                lock (s_lock)
                {
                    if (s_androidSdkVersion.HasValue) return s_androidSdkVersion.Value;
#if UNITY_ANDROID
                    if (Application.platform == RuntimePlatform.Android)
                    {
                        using (var build = new AndroidJavaClass("android.os.Build$VERSION"))
                            s_androidSdkVersion = build.GetStatic<int>("SDK_INT");
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
}
