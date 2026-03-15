////////////////////////////////////////////////////////////////////////////////
// MobileVibrationService
// Native mobile haptic feedback — Android, iOS (Core Haptics), WebGL.
// Zero-GC hot path via pre-allocated static buffers.
////////////////////////////////////////////////////////////////////////////////

using System;
using UnityEngine;

#if UNITY_IOS || UNITY_WEBGL
using System.Runtime.InteropServices;
#endif

namespace CycloneGames.DeviceFeedback.Runtime
{
    public sealed class MobileVibrationService : IMobileVibrationService
    {
        // ── Native imports ──

#if UNITY_IOS
        [DllImport("__Internal")] private static extern bool _HasVibrator();
        [DllImport("__Internal")] private static extern void _VibratePop();
        [DllImport("__Internal")] private static extern void _VibratePeek();
        [DllImport("__Internal")] private static extern void _VibrateNope();
        [DllImport("__Internal")] private static extern void _HapticFeedbackPrepare();
        [DllImport("__Internal")] private static extern void _impactOccurred(string style);
        [DllImport("__Internal")] private static extern void _notificationOccurred(string style);
        [DllImport("__Internal")] private static extern void _selectionChanged();

        // Core Haptics (iOS 13+)
        [DllImport("__Internal")] private static extern bool _CoreHapticsSupported();
        [DllImport("__Internal")] private static extern void _CoreHapticsInit();
        [DllImport("__Internal")] private static extern void _CoreHapticsDestroy();
        [DllImport("__Internal")] private static extern void _CoreHapticsPlayTransient(float intensity, float sharpness);
        [DllImport("__Internal")] private static extern void _CoreHapticsPlayContinuous(float intensity, float sharpness, float duration);
        [DllImport("__Internal")] private static extern void _CoreHapticsPlayPattern(float[] times, float[] intensities, float[] sharpnesses, int[] types, float[] durations, int count);
        [DllImport("__Internal")] private static extern void _CoreHapticsPlayCurves(float[] times, float[] intensities, float[] sharpnesses, int pointCount, float duration);
        [DllImport("__Internal")] private static extern void _CoreHapticsStop();
        [DllImport("__Internal")] private static extern void _CoreHapticsUpdateParameters(float intensity, float sharpness);
#endif

#if UNITY_WEBGL
        [DllImport("__Internal")] private static extern void VibrateWebgl(int ms);
#endif

        // ── Android JNI refs (cached for zero-GC) ──

#if UNITY_ANDROID
        private AndroidJavaObject _vibrator;
        private AndroidJavaClass _vibrationEffect;
        private int _sdkVersion = -1;
        private bool _compositionSupported;
#endif

        // ── Reusable buffers (zero-GC hot path) ──
        // Grown on demand, never shrunk — avoids per-call allocation.

        private static long[] s_timingBuf = Array.Empty<long>();
        private static int[] s_amplitudeBuf = Array.Empty<int>();
        private static float[] s_floatTimeBuf = Array.Empty<float>();
        private static float[] s_intensityBuf = Array.Empty<float>();
        private static float[] s_sharpnessBuf = Array.Empty<float>();
        private static int[] s_typeBuf = Array.Empty<int>();
        private static float[] s_durationBuf = Array.Empty<float>();

        private static void EnsureBuffers(int count)
        {
            if (s_timingBuf.Length >= count) return;
            int capacity = Math.Max(count, 64);
            s_timingBuf = new long[capacity];
            s_amplitudeBuf = new int[capacity];
            s_floatTimeBuf = new float[capacity];
            s_intensityBuf = new float[capacity];
            s_sharpnessBuf = new float[capacity];
            s_typeBuf = new int[capacity];
            s_durationBuf = new float[capacity];
        }

        // ── Preset patterns (static, never re-allocated) ──

        private static readonly long[] NopePattern = { 0, 50, 50, 50 };
        private static readonly long[] WarningPattern = { 0, 40, 60, 40 };
        private static readonly long[] ErrorPattern = { 0, 50, 40, 50, 40, 50 };

        private bool? _hasVibrator;
        private bool _initialized;
        private bool _disposed;
#if UNITY_IOS
        private bool _coreHapticsAvailable;

        // Pre-cached enum name strings — avoids ToString() GC allocation on every native interop call.
        private static readonly string[] s_impactStyleNames = { "Heavy", "Medium", "Light", "Rigid", "Soft" };
        private static readonly string[] s_notifStyleNames = { "Error", "Success", "Warning" };
#endif

        // ── IHapticFeedbackService ──

        public bool IsAvailable => HasVibrator;
        public bool IsActive { get; set; } = true;

        public void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

#if UNITY_ANDROID
            if (Application.platform != RuntimePlatform.Android) return;

            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                _vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
            }

            if (SdkVersion >= 26)
                _vibrationEffect = new AndroidJavaClass("android.os.VibrationEffect");

            _compositionSupported = SdkVersion >= 30;
#elif UNITY_IOS
            if (Application.platform != RuntimePlatform.IPhonePlayer) return;

            _HapticFeedbackPrepare();
            _coreHapticsAvailable = _CoreHapticsSupported();
            if (_coreHapticsAvailable)
                _CoreHapticsInit();
#endif
        }

        public void PlayPreset(HapticPreset preset)
        {
            if (!CanOperate()) return;

#if UNITY_ANDROID
            // API 30+: use device-tuned haptic primitives for best feel
            if (_compositionSupported && TryPlayAndroidPrimitive(preset))
                return;
#endif

            switch (preset)
            {
                case HapticPreset.Light:    VibrateImpactImpl("Light", 20, 0.4f, 0.8f);  break;
                case HapticPreset.Medium:   VibrateImpactImpl("Medium", 40, 0.6f, 0.5f); break;
                case HapticPreset.Heavy:    VibrateImpactImpl("Heavy", 80, 1.0f, 0.2f);  break;
                case HapticPreset.Success:  VibrateNotificationImpl("Success", 60);       break;
                case HapticPreset.Warning:  VibrateNotificationImpl("Warning", 50, WarningPattern); break;
                case HapticPreset.Error:    VibrateNotificationImpl("Error", 80, ErrorPattern);     break;
                case HapticPreset.Selection: VibrateSelectionImpl(10); break;
            }
        }

        public void Play(float normalizedIntensity, float durationSeconds, float sharpness = 0.5f)
        {
            if (!CanOperate()) return;

            normalizedIntensity = Mathf.Clamp01(normalizedIntensity);
            sharpness = Mathf.Clamp01(sharpness);
            if (normalizedIntensity <= 0f) return;

            long ms = (long)(durationSeconds * 1000f);
            if (ms <= 0) return;

#if UNITY_ANDROID
            int amplitude = Mathf.Clamp((int)(normalizedIntensity * 255), 1, 255);
            VibrateAndroid(ms, amplitude);
#elif UNITY_IOS
            if (_coreHapticsAvailable)
            {
                _CoreHapticsPlayContinuous(normalizedIntensity, sharpness, durationSeconds);
            }
            else
            {
                MapIntensityToImpact(normalizedIntensity);
            }
#elif UNITY_WEBGL
            VibrateWebgl(ClampToInt(ms));
#endif
        }

        public void PlayCurve(AnimationCurve intensityCurve, float durationSeconds,
                              AnimationCurve sharpnessCurve = null, int sampleIntervalMs = 20)
        {
            if (intensityCurve == null || durationSeconds <= 0f || !CanOperate()) return;
            sampleIntervalMs = Mathf.Max(sampleIntervalMs, 5);

            int sampleCount = Mathf.Max(1, Mathf.CeilToInt(durationSeconds * 1000f / sampleIntervalMs));
            EnsureBuffers(sampleCount);

            // Sample both curves into pre-allocated buffers
            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / sampleCount;
                float rawIntensity = Mathf.Clamp01(intensityCurve.Evaluate(t));
                s_timingBuf[i] = sampleIntervalMs;
                s_amplitudeBuf[i] = Mathf.Clamp((int)(rawIntensity * 255), 0, 255);
                s_floatTimeBuf[i] = t * durationSeconds;
                s_intensityBuf[i] = rawIntensity;
                s_sharpnessBuf[i] = sharpnessCurve != null ? Mathf.Clamp01(sharpnessCurve.Evaluate(t)) : 0.5f;
            }

#if UNITY_ANDROID
            PlayAndroidWaveform(sampleCount);
#elif UNITY_IOS
            if (_coreHapticsAvailable)
            {
                // Pass control points to native CHHapticParameterCurve for OS-level interpolation
                _CoreHapticsPlayCurves(s_floatTimeBuf, s_intensityBuf, s_sharpnessBuf, sampleCount, durationSeconds);
            }
            else
            {
                float peak = 0f;
                for (int i = 0; i < sampleCount; i++)
                    peak = Mathf.Max(peak, s_intensityBuf[i]);
                MapIntensityToImpact(peak);
            }
#elif UNITY_WEBGL
            long totalMs = 0;
            for (int i = 0; i < sampleCount; i++)
                if (s_amplitudeBuf[i] > 0) totalMs += s_timingBuf[i];
            if (totalMs > 0) VibrateWebgl(ClampToInt(totalMs));
#endif
        }

        public void PlayClip(HapticClip clip)
        {
            if (clip == null || !CanOperate()) return;

            if (clip.HasEvents)
                PlayClipEvents(clip);
            else
                PlayCurve(clip.intensityCurve, clip.duration, clip.sharpnessCurve);
        }

        public void Cancel()
        {
#if UNITY_ANDROID
            if (Application.platform == RuntimePlatform.Android)
                _vibrator?.Call("cancel");
#elif UNITY_IOS
            if (_coreHapticsAvailable)
                _CoreHapticsStop();
#elif UNITY_WEBGL
            if (Application.platform == RuntimePlatform.WebGLPlayer)
                VibrateWebgl(0);
#endif
        }

        // ── IMobileVibrationService ──

        public bool HasVibrator
        {
            get
            {
                _hasVibrator ??= DetectVibrator();
                return _hasVibrator.Value;
            }
        }

        public void Vibrate()
        {
            if (!CanOperate()) return;
#if UNITY_ANDROID || UNITY_IOS
            Handheld.Vibrate();
#elif UNITY_WEBGL
            VibrateWebgl(200);
#endif
        }

        public void Vibrate(long milliseconds)
        {
            if (!CanOperate()) return;
#if UNITY_ANDROID
            VibrateAndroid(milliseconds);
#elif UNITY_IOS
            if (_coreHapticsAvailable)
                _CoreHapticsPlayContinuous(0.8f, 0.5f, milliseconds / 1000f);
            else
                Handheld.Vibrate();
#elif UNITY_WEBGL
            VibrateWebgl(ClampToInt(milliseconds));
#endif
        }

        public void Vibrate(long[] pattern, int repeat = -1)
        {
            if (pattern == null || !CanOperate()) return;
#if UNITY_ANDROID
            VibrateAndroidPattern(pattern, repeat);
#elif UNITY_IOS
            Handheld.Vibrate();
#elif UNITY_WEBGL
            long total = 0;
            for (int i = 0; i < pattern.Length; i++) total += pattern[i];
            VibrateWebgl(ClampToInt(total));
#endif
        }

        public void VibratePop()
        {
            if (!CanOperate()) return;
#if UNITY_IOS
            if (_coreHapticsAvailable) _CoreHapticsPlayTransient(0.8f, 0.9f);
            else _VibratePop();
#elif UNITY_ANDROID
            VibrateAndroid(50);
#elif UNITY_WEBGL
            VibrateWebgl(50);
#endif
        }

        public void VibratePeek()
        {
            if (!CanOperate()) return;
#if UNITY_IOS
            if (_coreHapticsAvailable) _CoreHapticsPlayTransient(0.5f, 0.5f);
            else _VibratePeek();
#elif UNITY_ANDROID
            VibrateAndroid(100);
#elif UNITY_WEBGL
            VibrateWebgl(100);
#endif
        }

        public void VibrateNope()
        {
            if (!CanOperate()) return;
#if UNITY_IOS
            if (_coreHapticsAvailable)
            {
                // Three rapid transients simulating denial
                EnsureBuffers(3);
                s_floatTimeBuf[0] = 0f;     s_floatTimeBuf[1] = 0.1f;   s_floatTimeBuf[2] = 0.2f;
                s_intensityBuf[0] = 0.6f;   s_intensityBuf[1] = 0.6f;   s_intensityBuf[2] = 0.6f;
                s_sharpnessBuf[0] = 0.5f;   s_sharpnessBuf[1] = 0.5f;   s_sharpnessBuf[2] = 0.5f;
                s_typeBuf[0] = 0;            s_typeBuf[1] = 0;            s_typeBuf[2] = 0;
                s_durationBuf[0] = 0f;       s_durationBuf[1] = 0f;       s_durationBuf[2] = 0f;
                _CoreHapticsPlayPattern(s_floatTimeBuf, s_intensityBuf, s_sharpnessBuf, s_typeBuf, s_durationBuf, 3);
            }
            else
            {
                _VibrateNope();
            }
#elif UNITY_ANDROID
            VibrateAndroidPattern(NopePattern, -1);
#elif UNITY_WEBGL
            VibrateWebgl(150);
#endif
        }

        public void VibrateIOS(IOSImpactStyle style)
        {
            if (!CanOperate()) return;
#if UNITY_IOS
            if (_coreHapticsAvailable)
            {
                MapIOSImpactToCoreHaptics(style);
            }
            else
            {
                _impactOccurred(s_impactStyleNames[(int)style]);
            }
#endif
        }

        public void VibrateIOS(IOSNotificationStyle style)
        {
            if (!CanOperate()) return;
#if UNITY_IOS
            _notificationOccurred(s_notifStyleNames[(int)style]);
#endif
        }

        public void VibrateIOSSelection()
        {
            if (!CanOperate()) return;
#if UNITY_IOS
            if (_coreHapticsAvailable)
                _CoreHapticsPlayTransient(0.3f, 0.9f);
            else
                _selectionChanged();
#endif
        }

        /// <summary>
        /// Real-time parameter update on the currently playing haptic effect (iOS 13+ only).
        /// Call each frame to modulate the active continuous haptic in response to game state.
        /// </summary>
        public void UpdateContinuousParameters(float intensity, float sharpness)
        {
#if UNITY_IOS
            if (!CanOperate() || !_coreHapticsAvailable) return;
            _CoreHapticsUpdateParameters(Mathf.Clamp01(intensity), Mathf.Clamp01(sharpness));
#endif
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

#if UNITY_ANDROID
            _vibrationEffect?.Dispose();
            _vibrator?.Dispose();
            _vibrationEffect = null;
            _vibrator = null;
#elif UNITY_IOS
            if (_coreHapticsAvailable)
                _CoreHapticsDestroy();
#endif
        }

        #region Private

        private bool CanOperate() => !_disposed && IsActive && HasVibrator;

        private bool DetectVibrator()
        {
#if UNITY_ANDROID
            if (Application.platform != RuntimePlatform.Android) return false;
            return _vibrator != null && _vibrator.Call<bool>("hasVibrator");
#elif UNITY_IOS
            if (Application.platform != RuntimePlatform.IPhonePlayer) return false;
            return _HasVibrator();
#elif UNITY_WEBGL
            return Application.platform == RuntimePlatform.WebGLPlayer;
#else
            return false;
#endif
        }

        // ── Android ──

#if UNITY_ANDROID
        private int SdkVersion
        {
            get
            {
                if (_sdkVersion < 0)
                {
                    if (Application.platform == RuntimePlatform.Android)
                    {
                        using (var build = new AndroidJavaClass("android.os.Build$VERSION"))
                            _sdkVersion = build.GetStatic<int>("SDK_INT");
                    }
                    else
                    {
                        _sdkVersion = 0;
                    }
                }
                return _sdkVersion;
            }
        }

        private void VibrateAndroid(long milliseconds, int amplitude = -1)
        {
            if (_vibrator == null) return;

            if (SdkVersion >= 26)
            {
                using (var effect = _vibrationEffect.CallStatic<AndroidJavaObject>("createOneShot", milliseconds, amplitude))
                    _vibrator.Call("vibrate", effect);
            }
            else
            {
                _vibrator.Call("vibrate", milliseconds);
            }
        }

        private void VibrateAndroidPattern(long[] pattern, int repeat)
        {
            if (_vibrator == null) return;

            if (SdkVersion >= 26)
            {
                using (var effect = _vibrationEffect.CallStatic<AndroidJavaObject>("createWaveform", pattern, repeat))
                    _vibrator.Call("vibrate", effect);
            }
            else
            {
                _vibrator.Call("vibrate", pattern, repeat);
            }
        }

        // Exact-length waveform arrays for JNI — cached and resized only when sampleCount changes.
        private static long[] s_jniTimings = Array.Empty<long>();
        private static int[] s_jniAmplitudes = Array.Empty<int>();

        private void PlayAndroidWaveform(int sampleCount)
        {
            if (_vibrator == null) return;

            if (SdkVersion >= 26)
            {
                // createWaveform requires exact-length arrays; reuse cached arrays when size matches
                if (s_jniTimings.Length != sampleCount)
                {
                    s_jniTimings = new long[sampleCount];
                    s_jniAmplitudes = new int[sampleCount];
                }
                Array.Copy(s_timingBuf, s_jniTimings, sampleCount);
                Array.Copy(s_amplitudeBuf, s_jniAmplitudes, sampleCount);

                using (var effect = _vibrationEffect.CallStatic<AndroidJavaObject>("createWaveform", s_jniTimings, s_jniAmplitudes, -1))
                    _vibrator.Call("vibrate", effect);
            }
            else
            {
                BuildOnOffPattern(sampleCount, out long[] onOff);
                _vibrator.Call("vibrate", onOff, -1);
            }
        }

        // Pre-API 26: collapse amplitude waveform into binary on/off pattern.
        private static void BuildOnOffPattern(int sampleCount, out long[] onOffPattern)
        {
            const int threshold = 20;
            // Worst case: alternating on/off for every segment
            var list = new long[sampleCount * 2 + 1];
            int idx = 0;
            long pauseAccum = 0;
            long vibrateAccum = 0;

            for (int i = 0; i < sampleCount; i++)
            {
                if (s_amplitudeBuf[i] > threshold)
                {
                    if (vibrateAccum == 0)
                    {
                        list[idx++] = pauseAccum;
                        pauseAccum = 0;
                    }
                    vibrateAccum += s_timingBuf[i];
                }
                else
                {
                    if (vibrateAccum > 0)
                    {
                        list[idx++] = vibrateAccum;
                        vibrateAccum = 0;
                    }
                    pauseAccum += s_timingBuf[i];
                }
            }
            if (vibrateAccum > 0)
            {
                if (idx == 0) list[idx++] = 0;
                list[idx++] = vibrateAccum;
            }

            if (idx == 0)
            {
                onOffPattern = new long[] { 0, 1 };
                return;
            }
            onOffPattern = new long[idx];
            Array.Copy(list, onOffPattern, idx);
        }

        // Android API 30+ Composition API — device-tuned haptic primitives.
        // Primitives (CLICK=1, TICK=7, LOW_TICK=8, THUD=3) are calibrated per-device
        // by the OEM for the best possible feel at any given scale.
        private bool TryPlayAndroidPrimitive(HapticPreset preset)
        {
            int primitiveId;
            float scale;

            switch (preset)
            {
                case HapticPreset.Light:     primitiveId = 7;  scale = 0.4f; break; // TICK
                case HapticPreset.Medium:    primitiveId = 1;  scale = 0.6f; break; // CLICK
                case HapticPreset.Heavy:     primitiveId = 3;  scale = 1.0f; break; // THUD
                case HapticPreset.Selection: primitiveId = 8;  scale = 0.3f; break; // LOW_TICK
                case HapticPreset.Success:   primitiveId = 1;  scale = 0.8f; break; // CLICK
                case HapticPreset.Warning:   primitiveId = 1;  scale = 0.5f; break; // CLICK
                case HapticPreset.Error:     primitiveId = 3;  scale = 0.7f; break; // THUD
                default: return false;
            }

            try
            {
                using (var composition = _vibrationEffect.CallStatic<AndroidJavaObject>("startComposition"))
                {
                    composition.Call<AndroidJavaObject>("addPrimitive", primitiveId, scale, 0);
                    using (var effect = composition.Call<AndroidJavaObject>("compose"))
                        _vibrator.Call("vibrate", effect);
                }
                return true;
            }
            catch (Exception)
            {
                // Device may not support specific primitives; fall through to legacy path
                _compositionSupported = false;
                return false;
            }
        }
#endif

        // ── iOS helpers ──

#if UNITY_IOS
        private static void MapIntensityToImpact(float intensity)
        {
            if (intensity < 0.33f) _impactOccurred("Light");
            else if (intensity < 0.66f) _impactOccurred("Medium");
            else _impactOccurred("Heavy");
        }

        private static void MapIOSImpactToCoreHaptics(IOSImpactStyle style)
        {
            switch (style)
            {
                case IOSImpactStyle.Light:  _CoreHapticsPlayTransient(0.4f, 0.8f); break;
                case IOSImpactStyle.Medium: _CoreHapticsPlayTransient(0.6f, 0.5f); break;
                case IOSImpactStyle.Heavy:  _CoreHapticsPlayTransient(1.0f, 0.2f); break;
                case IOSImpactStyle.Rigid:  _CoreHapticsPlayTransient(0.8f, 1.0f); break;
                case IOSImpactStyle.Soft:   _CoreHapticsPlayTransient(0.5f, 0.1f); break;
            }
        }
#endif

        // ── Shared helpers ──

        private void VibrateImpactImpl(string iosLegacyStyle, int fallbackMs, float intensity, float sharpness)
        {
#if UNITY_IOS
            if (_coreHapticsAvailable)
                _CoreHapticsPlayTransient(intensity, sharpness);
            else
                _impactOccurred(iosLegacyStyle);
#elif UNITY_ANDROID
            int amp = SdkVersion >= 26 ? Mathf.Clamp((int)(intensity * 255), 1, 255) : -1;
            VibrateAndroid(fallbackMs, amp);
#elif UNITY_WEBGL
            VibrateWebgl(fallbackMs);
#endif
        }

        private void VibrateNotificationImpl(string iosStyle, int fallbackMs, long[] androidPattern = null)
        {
#if UNITY_IOS
            _notificationOccurred(iosStyle);
#elif UNITY_ANDROID
            if (androidPattern != null)
                VibrateAndroidPattern(androidPattern, -1);
            else
                VibrateAndroid(fallbackMs);
#elif UNITY_WEBGL
            VibrateWebgl(fallbackMs);
#endif
        }

        private void VibrateSelectionImpl(int fallbackMs)
        {
#if UNITY_IOS
            if (_coreHapticsAvailable)
                _CoreHapticsPlayTransient(0.3f, 0.9f);
            else
                _selectionChanged();
#elif UNITY_ANDROID
            VibrateAndroid(fallbackMs);
#elif UNITY_WEBGL
            VibrateWebgl(fallbackMs);
#endif
        }

        // Play HapticClip discrete events via native composite patterns.
        private void PlayClipEvents(HapticClip clip)
        {
            var events = clip.events;
            int count = events.Length;
            EnsureBuffers(count);

            for (int i = 0; i < count; i++)
            {
                ref readonly var e = ref events[i];
                s_floatTimeBuf[i] = e.time;
                s_intensityBuf[i] = Mathf.Clamp01(e.intensity);
                s_sharpnessBuf[i] = Mathf.Clamp01(e.sharpness);
                s_typeBuf[i] = (int)e.type;
                s_durationBuf[i] = e.duration;
            }

#if UNITY_IOS
            if (_coreHapticsAvailable)
            {
                _CoreHapticsPlayPattern(s_floatTimeBuf, s_intensityBuf, s_sharpnessBuf, s_typeBuf, s_durationBuf, count);
                return;
            }
#endif

#if UNITY_ANDROID
            // Convert events to waveform: each event becomes one segment
            for (int i = 0; i < count; i++)
            {
                float dur = events[i].type == HapticEventType.Transient ? 0.03f : events[i].duration;
                s_timingBuf[i] = Math.Max(1L, (long)(dur * 1000f));
                s_amplitudeBuf[i] = Mathf.Clamp((int)(s_intensityBuf[i] * 255), 0, 255);
            }
            PlayAndroidWaveform(count);
#elif UNITY_WEBGL
            long total = 0;
            for (int i = 0; i < count; i++)
            {
                float dur = events[i].type == HapticEventType.Transient ? 0.03f : events[i].duration;
                total += (long)(dur * 1000f);
            }
            if (total > 0) VibrateWebgl(ClampToInt(total));
#endif
        }

        private static int ClampToInt(long value) => (int)Math.Min(value, int.MaxValue);

        #endregion
    }
}
