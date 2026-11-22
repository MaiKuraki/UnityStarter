// All scratch buffers are instance-owned. Capacity growth is amortized, and
// platform arrays requiring an exact Length come from a bounded cache.
// Every playback call passes through one admission path and publishes a
// FeedbackResult. Authoring assets are never mutated at runtime.

using System;
using System.Threading;
using UnityEngine;

#if UNITY_IOS || UNITY_WEBGL
using System.Runtime.InteropServices;
#endif

namespace CycloneGames.DeviceFeedback.Runtime
{
    public sealed class MobileVibrationService : IMobileVibrationService, IFeedbackResultProvider
    {

#if UNITY_IOS
        [DllImport("__Internal")] private static extern bool _HasVibrator();
        [DllImport("__Internal")] private static extern void _VibratePop();
        [DllImport("__Internal")] private static extern void _VibratePeek();
        [DllImport("__Internal")] private static extern void _VibrateNope();
        [DllImport("__Internal")] private static extern void _HapticFeedbackPrepare();
        [DllImport("__Internal")] private static extern void _impactOccurred(string style);
        [DllImport("__Internal")] private static extern void _notificationOccurred(string style);
        [DllImport("__Internal")] private static extern void _selectionChanged();

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

#if UNITY_ANDROID
        private AndroidJavaObject _vibrator;
        private AndroidJavaClass _vibrationEffect;
        private int _sdkVersion = -1;
        private int _compositionFailureCount;
        private bool _compositionDegraded;
        private const int CompositionFailureThreshold = 3;
#endif

#if UNITY_IOS
        private bool _coreHapticsAvailable;
        private static readonly string[] s_impactStyleNames = { "Heavy", "Medium", "Light", "Rigid", "Soft" };
        private static readonly string[] s_notifStyleNames = { "Error", "Success", "Warning" };
#endif

        private static readonly long[] NopePattern = { 0, 50, 50, 50 };
        private static readonly long[] WarningPattern = { 0, 40, 60, 40 };
        private static readonly long[] ErrorPattern = { 0, 50, 40, 50, 40, 50 };

        private readonly DeviceFeedbackBufferLease _buffers;
        private readonly FeedbackCommandQueue _commands = new FeedbackCommandQueue(256);
        private readonly System.Collections.Generic.Dictionary<HapticClip, HapticClipSnapshot> _clipCache;
        private readonly Action<FeedbackCommand> _drainAction;
        private readonly DeviceFeedbackCounters _counters = new DeviceFeedbackCounters();
        private readonly DeviceFeedbackLimits _limits;
        private readonly DeviceFeedbackProfile _profile;
        private DeviceCapability? _capability;
        private FeedbackResult _lastResult;
        private bool _initialized;
        private bool _disposed;
        private bool _isActive = true;
        private int _ownerThreadId;

        public MobileVibrationService() : this(DeviceFeedbackLimits.Default)
        {
        }

        public MobileVibrationService(DeviceFeedbackLimits limits)
            : this(limits, DeviceFeedbackProfile.Default)
        {
        }

        public MobileVibrationService(DeviceFeedbackLimits limits, DeviceFeedbackProfile profile)
        {
            _limits = limits.Normalize();
            _profile = profile;
            _buffers = new DeviceFeedbackBufferLease(in profile);
            _clipCache = new System.Collections.Generic.Dictionary<HapticClip, HapticClipSnapshot>(
                profile.ClipCacheMaxEntries);
            _drainAction = DispatchCommand;
        }

        public FeedbackResult LastResult => _lastResult;
        public DeviceFeedbackLimits Limits => _limits;

        public DeviceCapability Capability
        {
            get
            {
                if (!_capability.HasValue) _capability = DetectCapability();
                return _capability.Value;
            }
        }

        public bool IsAvailable => !_disposed && _isActive && Capability.SupportsHaptics;
        public bool HasVibrator => Capability.SupportsHaptics;

        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (_isActive == value) return;
                _isActive = value;
                if (!_isActive) StopNative();
            }
        }

        public DeviceFeedbackMemoryStats GetMemoryStats() => _counters.GetStats(_buffers.GetStats());

        internal DeviceFeedbackBufferStats GetBufferStats() => _buffers.GetStats();

        public void ResetDiagnostics() => _counters.Reset();

        public void RefreshCapabilities()
        {
            _capability = null;
#if UNITY_ANDROID
            _sdkVersion = -1;
            _compositionDegraded = false;
            _compositionFailureCount = 0;
#endif
        }

        /// <summary>
        /// Idempotent initialization. Calling it after Dispose is a no-op;
        /// use Reinitialize() when an existing instance must be revived.
        /// </summary>
        public void Initialize()
        {
            if (_initialized || _disposed) return;
            _initialized = true;

#if UNITY_ANDROID
            if (Application.platform != RuntimePlatform.Android) return;
            using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                _vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
            }
            if (SdkVersion >= 26) _vibrationEffect = new AndroidJavaClass("android.os.VibrationEffect");
#elif UNITY_IOS
            if (Application.platform != RuntimePlatform.IPhonePlayer) return;
            _HapticFeedbackPrepare();
            _coreHapticsAvailable = _CoreHapticsSupported();
            if (_coreHapticsAvailable) _CoreHapticsInit();
#endif
        }

        /// <summary>
        /// Explicitly revives a disposed instance. Buffers are reused when they
        /// are still owned by this instance and are regrown on first use.
        /// </summary>
        public void Reinitialize()
        {
            _disposed = false;
            _initialized = false;
            Volatile.Write(ref _ownerThreadId, 0);
            RefreshCapabilities();
            Initialize();
        }

        public void PlayPreset(HapticPreset preset) => PlayPresetChecked(preset);

        public FeedbackResult PlayPresetChecked(HapticPreset preset)
        {
            FeedbackStatus guard = Guard();
            if (guard != FeedbackStatus.Accepted) return Finish(FeedbackResult.Rejected(guard));
            if (!IsDefined(preset)) return Finish(FeedbackResult.Rejected(FeedbackStatus.RejectedInvalid));

            ref readonly PresetPayload payload = ref PresetTable.Payloads[(int)preset];
            long durationMilliseconds = payload.DurationMilliseconds;
            DeviceFeedbackAdmissionFailure failure = DeviceFeedbackAdmission.ValidateDurationMilliseconds(
                durationMilliseconds, in _limits);
            if (failure != DeviceFeedbackAdmissionFailure.None)
            {
                return Finish(FeedbackResult.Rejected(DeviceFeedbackAdmission.ToStatus(failure)));
            }

            FeedbackResult result = FeedbackResult.Accepted(durationMilliseconds);
            bool dispatched = false;
            bool degraded = false;

#if UNITY_ANDROID
            if (SdkVersion >= 30 && Capability.SupportsAmplitude && !_compositionDegraded)
            {
                if (TryPlayAndroidPrimitive(preset)) dispatched = true;
                else degraded = true; // platform failure is observable and falls back
            }
#endif
            if (!dispatched)
            {
                degraded = DispatchPreset(preset) || degraded;
            }

            return Finish(degraded ? FeedbackResult.Degraded(durationMilliseconds) : result);
        }

        public void Play(float normalizedIntensity, float durationSeconds, float sharpness = 0.5f)
        {
            PlayChecked(normalizedIntensity, durationSeconds, sharpness);
        }

        public FeedbackResult PlayChecked(float normalizedIntensity, float durationSeconds, float sharpness = 0.5f)
        {
            FeedbackStatus guard = Guard();
            if (guard != FeedbackStatus.Accepted) return Finish(FeedbackResult.Rejected(guard));
            if (!DeviceFeedbackAdmission.IsFinite(normalizedIntensity) ||
                !DeviceFeedbackAdmission.IsFinite(sharpness))
            {
                return Finish(FeedbackResult.Rejected(FeedbackStatus.RejectedInvalid));
            }

            DeviceFeedbackAdmissionFailure failure = DeviceFeedbackAdmission.ValidateDurationSeconds(
                durationSeconds, in _limits, out long durationMilliseconds);
            if (failure != DeviceFeedbackAdmissionFailure.None)
            {
                return Finish(FeedbackResult.Rejected(DeviceFeedbackAdmission.ToStatus(failure)));
            }
            if (normalizedIntensity <= 0f)
            {
                return Finish(FeedbackResult.Rejected(FeedbackStatus.RejectedInvalid));
            }

            float intensity = Mathf.Clamp01(normalizedIntensity);
            float crispness = Mathf.Clamp01(sharpness);
            bool degraded = DispatchPlay(intensity, durationSeconds, crispness);
            return Finish(degraded
                ? FeedbackResult.Degraded(durationMilliseconds)
                : FeedbackResult.Accepted(durationMilliseconds));
        }

        public void PlayCurve(AnimationCurve intensityCurve, float durationSeconds,
                              AnimationCurve sharpnessCurve = null, int sampleIntervalMs = 20)
        {
            PlayCurveChecked(intensityCurve, durationSeconds, sharpnessCurve, sampleIntervalMs);
        }

        public FeedbackResult PlayCurveChecked(AnimationCurve intensityCurve, float durationSeconds,
                                               AnimationCurve sharpnessCurve = null, int sampleIntervalMs = 20)
        {
            FeedbackStatus guard = Guard();
            if (guard != FeedbackStatus.Accepted) return Finish(FeedbackResult.Rejected(guard));
            if (intensityCurve == null)
            {
                return Finish(FeedbackResult.Rejected(FeedbackStatus.RejectedInvalid));
            }

            int interval = DeviceFeedbackAdmission.SanitizeSampleInterval(sampleIntervalMs, in _profile);
            DeviceFeedbackAdmissionFailure failure = DeviceFeedbackAdmission.CalculateSampleCount(
                durationSeconds, interval, in _limits, in _profile,
                out long durationMilliseconds, out int sampleCount);
            if (failure != DeviceFeedbackAdmissionFailure.None)
            {
                return Finish(FeedbackResult.Rejected(DeviceFeedbackAdmission.ToStatus(failure)));
            }

            _buffers.Ensure(sampleCount);
            int effectiveInterval = DeviceFeedbackAdmission.EffectiveIntervalMilliseconds(
                durationMilliseconds, sampleCount, in _profile);
            for (int i = 0; i < sampleCount; i++)
            {
                float t = sampleCount == 1 ? 1f : (float)i / (sampleCount - 1);
                float intensity = Mathf.Clamp01(intensityCurve.Evaluate(t));
                if (!DeviceFeedbackAdmission.IsFinite(intensity)) intensity = 0f;
                _buffers.Timing[i] = effectiveInterval;
                _buffers.Amplitude[i] = Mathf.Clamp((int)(intensity * 255f), 0, 255);
                _buffers.FloatTime[i] = t * durationSeconds;
                _buffers.Intensity[i] = intensity;
                float sharp = sharpnessCurve != null ? Mathf.Clamp01(sharpnessCurve.Evaluate(t)) : 0.5f;
                _buffers.Sharpness[i] = DeviceFeedbackAdmission.IsFinite(sharp) ? sharp : 0.5f;
            }

            bool degraded = DispatchWaveform(sampleCount, durationSeconds);
            return Finish(degraded
                ? FeedbackResult.Degraded(durationMilliseconds, sampleCount)
                : FeedbackResult.Accepted(durationMilliseconds, sampleCount));
        }

        public void PlayClip(HapticClip clip) => PlayClipChecked(clip);

        public FeedbackResult PlayClipChecked(HapticClip clip)
        {
            if (clip == null)
            {
                return Finish(FeedbackResult.Rejected(FeedbackStatus.RejectedInvalid));
            }

            // Bake once per clip and keep the snapshot in a bounded cache.
            HapticClipSnapshot snapshot;
            if (!_clipCache.TryGetValue(clip, out snapshot))
            {
                if (!HapticClipBaker.TryBake(clip, in _limits, in _profile, out snapshot, out FeedbackResult bakeResult))
                {
                    return Finish(bakeResult);
                }
                CacheSnapshot(clip, snapshot);
            }
            return PlayClipSnapshotChecked(snapshot);
        }
        private void CacheSnapshot(HapticClip clip, HapticClipSnapshot snapshot)
        {
            if (_clipCache.Count >= _profile.ClipCacheMaxEntries)
            {
                _clipCache.Clear();
            }
            _clipCache[clip] = snapshot;
        }

        /// <summary>
        /// Plays a pre-baked clip without sampling, sorting or buffer growth.
        /// The exact-length arrays are passed to the platform boundary directly.
        /// </summary>
        public void PlayClipSnapshot(HapticClipSnapshot snapshot) => PlayClipSnapshotChecked(snapshot);
        public FeedbackResult PlayClipSnapshotChecked(HapticClipSnapshot snapshot)
        {
            FeedbackStatus guard = Guard();
            if (guard != FeedbackStatus.Accepted) return Finish(FeedbackResult.Rejected(guard));
            if (snapshot == null || !snapshot.IsValid)
            {
                return Finish(FeedbackResult.Rejected(FeedbackStatus.RejectedInvalid));
            }

#if UNITY_IOS
            if (Capability.SupportsCoreHaptics)
            {
                if (snapshot.IsEventMode)
                {
                    _CoreHapticsPlayPattern(snapshot.Times, snapshot.Intensities, snapshot.Sharpnesses,
                                            snapshot.Types, snapshot.Durations, snapshot.EventCount);
                }
                else
                {
                    _CoreHapticsPlayCurves(snapshot.Times, snapshot.Intensities, snapshot.Sharpnesses,
                                           snapshot.SampleCount, snapshot.DurationMilliseconds / 1000f);
                }
            }
            else
            {
                DispatchImpactPreset("Medium", (int)snapshot.DurationMilliseconds, 0.6f, 0.5f);
            }
#elif UNITY_ANDROID
            if (SdkVersion >= 26)
            {
                using (var effect = _vibrationEffect.CallStatic<AndroidJavaObject>(
                    "createWaveform", snapshot.Timings, snapshot.Amplitudes, -1))
                {
                    _vibrator.Call("vibrate", effect);
                }
            }
            else
            {
                VibrateAndroid(snapshot.DurationMilliseconds);
            }
#elif UNITY_WEBGL
            if (snapshot.TotalVibrationMilliseconds > 0L)
            {
                VibrateWebgl(ClampToInt(snapshot.TotalVibrationMilliseconds));
            }
#endif
            return Finish(FeedbackResult.Accepted(
                snapshot.DurationMilliseconds,
                snapshot.SampleCount,
                snapshot.EventCount,
                snapshot.SegmentCount));
        }

        /// <summary>Thread-safe cross-thread submission. See Drain for delivery.</summary>
        public bool TrySubmit(in FeedbackCommand command)
        {
            if (_disposed || _commands == null) return false;
            if (!DeviceFeedbackAdmission.IsFinite(command.A) ||
                !DeviceFeedbackAdmission.IsFinite(command.B) ||
                !DeviceFeedbackAdmission.IsFinite(command.C) ||
                command.DurationMilliseconds < 0L)
            {
                return false;
            }
            bool accepted = _commands.TryEnqueue(in command);
            if (accepted) DeviceFeedbackDiagnostics.RecordAccepted(0L, 0, 0, 0);
            else DeviceFeedbackDiagnostics.RecordRejected(FeedbackStatus.RejectedCapacity);
            return accepted;
        }

        /// <summary>
        /// Delivers queued commands on the owner thread. Called once per frame
        /// by the composition root; the budget bounds per-frame work.
        /// </summary>
        public int Drain(int maxCommands = 64)
        {
            if (_disposed) return 0;
            int current = Environment.CurrentManagedThreadId;
            int owner = Volatile.Read(ref _ownerThreadId);
            if (owner != 0 && owner != current) return 0;
            return _commands.Drain(_drainAction, maxCommands);
        }

        public DeviceFeedbackCommandStats GetCommandStats() => _commands.GetStats();

        private void DispatchCommand(FeedbackCommand command)
        {
            switch (command.Kind)
            {
                case FeedbackCommandKind.Play:
                    PlayChecked(command.A, command.DurationMilliseconds / 1000f, command.B);
                    break;
                case FeedbackCommandKind.PlayPreset:
                    PlayPresetChecked((HapticPreset)command.Index);
                    break;
                case FeedbackCommandKind.PlayClipSnapshot:
                    PlayClipSnapshotChecked(command.Snapshot);
                    break;
                case FeedbackCommandKind.VibrateMilliseconds:
                    VibrateChecked(command.DurationMilliseconds);
                    break;
                case FeedbackCommandKind.Cancel:
                    Cancel();
                    break;
            }
        }

        public void Cancel()
        {
            if (_disposed)
            {
                Finish(FeedbackResult.Rejected(FeedbackStatus.RejectedDisposed));
                return;
            }
            StopNative();
            Finish(FeedbackResult.Accepted(0L));
        }

        public void Vibrate() => Vibrate(200L);

        public void Vibrate(long milliseconds) => VibrateChecked(milliseconds);

        public FeedbackResult VibrateChecked(long milliseconds)
        {
            FeedbackStatus guard = Guard();
            if (guard != FeedbackStatus.Accepted) return Finish(FeedbackResult.Rejected(guard));
            DeviceFeedbackAdmissionFailure failure = DeviceFeedbackAdmission.ValidateDurationMilliseconds(
                milliseconds, in _limits);
            if (failure != DeviceFeedbackAdmissionFailure.None)
            {
                return Finish(FeedbackResult.Rejected(DeviceFeedbackAdmission.ToStatus(failure)));
            }

            bool degraded = DispatchOneShot(milliseconds);
            return Finish(degraded
                ? FeedbackResult.Degraded(milliseconds)
                : FeedbackResult.Accepted(milliseconds));
        }

        public void Vibrate(long[] pattern, int repeat = -1) => VibratePatternChecked(pattern, repeat);

        public FeedbackResult VibratePatternChecked(long[] pattern, int repeat = -1)
        {
            FeedbackStatus guard = Guard();
            if (guard != FeedbackStatus.Accepted) return Finish(FeedbackResult.Rejected(guard));
            DeviceFeedbackAdmissionFailure failure = ValidatePattern(pattern, repeat, out long totalMilliseconds);
            if (failure != DeviceFeedbackAdmissionFailure.None)
            {
                return Finish(FeedbackResult.Rejected(DeviceFeedbackAdmission.ToStatus(failure)));
            }

            bool degraded = DispatchPattern(pattern, repeat, totalMilliseconds);
            return Finish(degraded
                ? FeedbackResult.Degraded(totalMilliseconds, patternSegmentCount: pattern.Length)
                : FeedbackResult.Accepted(totalMilliseconds, patternSegmentCount: pattern.Length));
        }

        public void VibratePop() => FixedTransient(50L, 0.8f, 0.9f, 0 /* pop */);
        public void VibratePeek() => FixedTransient(100L, 0.5f, 0.5f, 1 /* peek */);
        public void VibrateNope() => FixedTransient(150L, 0.6f, 0.5f, 2 /* nope */);

        public void VibrateIOS(IOSImpactStyle style)
        {
            FeedbackStatus guard = Guard();
            if (guard != FeedbackStatus.Accepted) { Finish(FeedbackResult.Rejected(guard)); return; }
            if (!IsDefinedImpact(style))
            {
                Finish(FeedbackResult.Rejected(FeedbackStatus.RejectedInvalid));
                return;
            }
            DispatchImpact(style);
            Finish(FeedbackResult.Accepted(0L));
        }

        public void VibrateIOS(IOSNotificationStyle style)
        {
            FeedbackStatus guard = Guard();
            if (guard != FeedbackStatus.Accepted) { Finish(FeedbackResult.Rejected(guard)); return; }
            if (!IsDefinedNotification(style))
            {
                Finish(FeedbackResult.Rejected(FeedbackStatus.RejectedInvalid));
                return;
            }
            DispatchNotification(style);
            Finish(FeedbackResult.Accepted(0L));
        }

        public void VibrateIOSSelection()
        {
            FeedbackStatus guard = Guard();
            if (guard != FeedbackStatus.Accepted) { Finish(FeedbackResult.Rejected(guard)); return; }
            DispatchSelection();
            Finish(FeedbackResult.Accepted(0L));
        }

        public void UpdateContinuousParameters(float intensity, float sharpness)
        {
            FeedbackStatus guard = Guard();
            if (guard != FeedbackStatus.Accepted) { Finish(FeedbackResult.Rejected(guard)); return; }
            if (!DeviceFeedbackAdmission.IsFinite(intensity) || !DeviceFeedbackAdmission.IsFinite(sharpness))
            {
                Finish(FeedbackResult.Rejected(FeedbackStatus.RejectedInvalid));
                return;
            }
#if UNITY_IOS
            if (!Capability.SupportsContinuousModulation)
            {
                Finish(FeedbackResult.Rejected(FeedbackStatus.RejectedUnsupported));
                return;
            }
            _CoreHapticsUpdateParameters(Mathf.Clamp01(intensity), Mathf.Clamp01(sharpness));
            Finish(FeedbackResult.Accepted(0L));
#else
            Finish(FeedbackResult.Rejected(FeedbackStatus.RejectedUnsupported));
#endif
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopNative();
#if UNITY_ANDROID
            _vibrationEffect?.Dispose();
            _vibrator?.Dispose();
            _vibrationEffect = null;
            _vibrator = null;
#elif UNITY_IOS
            if (_coreHapticsAvailable) _CoreHapticsDestroy();
            _coreHapticsAvailable = false;
#endif
            _buffers.Dispose();
            _clipCache.Clear();
            _commands.Clear();
        }

        #region Private

        private FeedbackStatus Guard()
        {
            // Buffers, counters and native handles belong to one owner thread.
            // Reject other threads before they touch instance state.
            int current = Environment.CurrentManagedThreadId;
            int owner = Volatile.Read(ref _ownerThreadId);
            if (owner == 0)
            {
                Interlocked.CompareExchange(ref _ownerThreadId, current, 0);
                owner = Volatile.Read(ref _ownerThreadId);
            }
            if (owner != current) return FeedbackStatus.RejectedThreadAffinity;
            if (_disposed) return FeedbackStatus.RejectedDisposed;
            if (!_isActive) return FeedbackStatus.RejectedInactive;
            if (!Capability.SupportsHaptics) return FeedbackStatus.RejectedUnavailable;
            return FeedbackStatus.Accepted;
        }

        private FeedbackResult Finish(FeedbackResult result)
        {
            // Foreign-thread rejections only touch the process-wide aggregate.
            if (result.Status == FeedbackStatus.RejectedThreadAffinity)
            {
                DeviceFeedbackDiagnostics.RecordRejected(result.Status);
                return result;
            }
            _lastResult = result;
            if (result.Status == FeedbackStatus.Degraded)
            {
                _counters.RecordDegraded(
                    result.DurationMilliseconds,
                    result.WaveformSampleCount,
                    result.HapticEventCount,
                    result.PatternSegmentCount);
            }
            else if (result.IsRejected)
            {
                _counters.RecordRejected(result.Status);
            }
            else
            {
                _counters.RecordAccepted(
                    result.DurationMilliseconds,
                    result.WaveformSampleCount,
                    result.HapticEventCount,
                    result.PatternSegmentCount);
            }
            return result;
        }

        private void FixedTransient(long milliseconds, float intensity, float sharpness, int kind)
        {
            FeedbackStatus guard = Guard();
            if (guard != FeedbackStatus.Accepted) { Finish(FeedbackResult.Rejected(guard)); return; }
            DeviceFeedbackAdmissionFailure failure = DeviceFeedbackAdmission.ValidateDurationMilliseconds(
                milliseconds, in _limits);
            if (failure != DeviceFeedbackAdmissionFailure.None)
            {
                Finish(FeedbackResult.Rejected(DeviceFeedbackAdmission.ToStatus(failure)));
                return;
            }

#if UNITY_IOS
            if (Capability.SupportsCoreHaptics)
            {
                if (kind == 2)
                {
                    _buffers.Ensure(3);
                    _buffers.FloatTime[0] = 0f; _buffers.FloatTime[1] = 0.1f; _buffers.FloatTime[2] = 0.2f;
                    _buffers.Intensity[0] = intensity; _buffers.Intensity[1] = intensity; _buffers.Intensity[2] = intensity;
                    _buffers.Sharpness[0] = sharpness; _buffers.Sharpness[1] = sharpness; _buffers.Sharpness[2] = sharpness;
                    _buffers.Type[0] = 0; _buffers.Type[1] = 0; _buffers.Type[2] = 0;
                    _buffers.Duration[0] = 0f; _buffers.Duration[1] = 0f; _buffers.Duration[2] = 0f;
                    _CoreHapticsPlayPattern(_buffers.FloatTime, _buffers.Intensity, _buffers.Sharpness,
                                            _buffers.Type, _buffers.Duration, 3);
                }
                else
                {
                    _CoreHapticsPlayTransient(intensity, sharpness);
                }
            }
            else if (kind == 0) _VibratePop();
            else if (kind == 1) _VibratePeek();
            else _VibrateNope();
#elif UNITY_ANDROID
            if (kind == 2) VibrateAndroidPattern(NopePattern, -1);
            else VibrateAndroid(milliseconds);
#elif UNITY_WEBGL
            VibrateWebgl(ClampToInt(milliseconds));
#endif
            Finish(FeedbackResult.Accepted(milliseconds));
        }

        private bool DispatchPlay(float intensity, float durationSeconds, float sharpness)
        {
#if UNITY_ANDROID
            int amplitude = Mathf.Clamp((int)(intensity * 255f), 1, 255);
            VibrateAndroid((long)(durationSeconds * 1000d), amplitude);
            return false;
#elif UNITY_IOS
            if (Capability.SupportsCoreHaptics)
            {
                _CoreHapticsPlayContinuous(intensity, sharpness, durationSeconds);
                return false;
            }
            MapIntensityToImpact(intensity);
            return true;
#elif UNITY_WEBGL
            VibrateWebgl(ClampToInt((long)(durationSeconds * 1000d)));
            return true;
#else
            return true;
#endif
        }

        private bool DispatchOneShot(long milliseconds)
        {
#if UNITY_ANDROID
            VibrateAndroid(milliseconds);
            return false;
#elif UNITY_IOS
            if (Capability.SupportsCoreHaptics)
            {
                _CoreHapticsPlayContinuous(0.8f, 0.5f, milliseconds / 1000f);
                return false;
            }
            Handheld.Vibrate();
            return true;
#elif UNITY_WEBGL
            VibrateWebgl(ClampToInt(milliseconds));
            return true;
#else
            return true;
#endif
        }

        private bool DispatchWaveform(int sampleCount, float durationSeconds)
        {
#if UNITY_ANDROID
            PlayAndroidWaveform(sampleCount);
            return false;
#elif UNITY_IOS
            if (Capability.SupportsCoreHaptics)
            {
                _CoreHapticsPlayCurves(_buffers.FloatTime, _buffers.Intensity, _buffers.Sharpness,
                                       sampleCount, durationSeconds);
                return false;
            }
            float peak = 0f;
            for (int i = 0; i < sampleCount; i++) peak = Mathf.Max(peak, _buffers.Intensity[i]);
            MapIntensityToImpact(peak);
            return true;
#elif UNITY_WEBGL
            long total = 0;
            for (int i = 0; i < sampleCount; i++)
            {
                if (_buffers.Amplitude[i] > 0) total += _buffers.Timing[i];
            }
            if (total > 0) VibrateWebgl(ClampToInt(total));
            return true;
#else
            return true;
#endif
        }

        private bool DispatchPreset(HapticPreset preset)
        {
            bool degraded = false;
            switch (preset)
            {
                case HapticPreset.Light: degraded = DispatchImpactPreset("Light", 20, 0.4f, 0.8f); break;
                case HapticPreset.Medium: degraded = DispatchImpactPreset("Medium", 40, 0.6f, 0.5f); break;
                case HapticPreset.Heavy: degraded = DispatchImpactPreset("Heavy", 80, 1f, 0.2f); break;
                case HapticPreset.Success: degraded = DispatchNotificationPreset("Success", 60, null); break;
                case HapticPreset.Warning: degraded = DispatchNotificationPreset("Warning", 50, WarningPattern); break;
                case HapticPreset.Error: degraded = DispatchNotificationPreset("Error", 80, ErrorPattern); break;
                case HapticPreset.Selection: degraded = DispatchSelectionPreset(10); break;
            }
            return degraded;
        }

        private bool DispatchImpactPreset(string fallbackStyle, int fallbackMs, float intensity, float sharpness)
        {
#if UNITY_IOS
            if (Capability.SupportsCoreHaptics) _CoreHapticsPlayTransient(intensity, sharpness);
            else _impactOccurred(fallbackStyle);
            return false;
#elif UNITY_ANDROID
            int amplitude = SdkVersion >= 26 ? Mathf.Clamp((int)(intensity * 255f), 1, 255) : -1;
            VibrateAndroid(fallbackMs, amplitude);
            return false;
#elif UNITY_WEBGL
            VibrateWebgl(fallbackMs);
            return true;
#else
            return true;
#endif
        }

        private bool DispatchNotificationPreset(string fallbackStyle, int fallbackMs, long[] pattern)
        {
#if UNITY_IOS
            if (Capability.SupportsCoreHaptics)
            {
                if (fallbackStyle == "Success") _CoreHapticsPlayTransient(0.7f, 0.6f);
                else if (fallbackStyle == "Warning") _CoreHapticsPlayTransient(0.5f, 0.4f);
                else _CoreHapticsPlayTransient(0.8f, 0.9f);
            }
            else _notificationOccurred(fallbackStyle);
            return false;
#elif UNITY_ANDROID
            if (pattern != null) VibrateAndroidPattern(pattern, -1);
            else VibrateAndroid(fallbackMs);
            return false;
#elif UNITY_WEBGL
            VibrateWebgl(fallbackMs);
            return true;
#else
            return true;
#endif
        }

        private bool DispatchSelectionPreset(int fallbackMs)
        {
#if UNITY_IOS
            if (Capability.SupportsCoreHaptics) _CoreHapticsPlayTransient(0.3f, 0.9f);
            else _selectionChanged();
            return false;
#elif UNITY_ANDROID
            VibrateAndroid(fallbackMs);
            return false;
#elif UNITY_WEBGL
            VibrateWebgl(fallbackMs);
            return true;
#else
            return true;
#endif
        }

        private bool DispatchPattern(long[] pattern, int repeat, long totalMilliseconds)
        {
#if UNITY_ANDROID
            VibrateAndroidPattern(pattern, repeat);
            return false;
#elif UNITY_IOS
            if (Capability.SupportsCoreHaptics && repeat == -1)
            {
                int eventCount = pattern.Length / 2;
                if (eventCount > 0)
                {
                    _buffers.Ensure(eventCount);
                    double timeSeconds = 0d;
                    int write = 0;
                    for (int i = 0; i + 1 < pattern.Length; i += 2)
                    {
                        timeSeconds += pattern[i] / 1000d;
                        if (pattern[i + 1] <= 0) continue;
                        _buffers.FloatTime[write] = (float)timeSeconds;
                        _buffers.Intensity[write] = 0.7f;
                        _buffers.Sharpness[write] = 0.6f;
                        _buffers.Type[write] = 1;
                        _buffers.Duration[write] = (float)(pattern[i + 1] / 1000d);
                        write++;
                        timeSeconds += pattern[i + 1] / 1000d;
                    }
                    if (write > 0)
                    {
                        _CoreHapticsPlayPattern(_buffers.FloatTime, _buffers.Intensity, _buffers.Sharpness,
                                                _buffers.Type, _buffers.Duration, write);
                        return false;
                    }
                }
            }
            Handheld.Vibrate();
            return true;
#elif UNITY_WEBGL
            long total = 0;
            for (int i = 1; i < pattern.Length; i += 2) total += pattern[i];
            if (total > 0) VibrateWebgl(ClampToInt(total));
            return true;
#else
            return true;
#endif
        }

        private void DispatchImpact(IOSImpactStyle style)
        {
#if UNITY_IOS
            if (Capability.SupportsCoreHaptics)
            {
                switch (style)
                {
                    case IOSImpactStyle.Light: _CoreHapticsPlayTransient(0.4f, 0.8f); break;
                    case IOSImpactStyle.Medium: _CoreHapticsPlayTransient(0.6f, 0.5f); break;
                    case IOSImpactStyle.Heavy: _CoreHapticsPlayTransient(1f, 0.2f); break;
                    case IOSImpactStyle.Rigid: _CoreHapticsPlayTransient(0.8f, 1f); break;
                    case IOSImpactStyle.Soft: _CoreHapticsPlayTransient(0.5f, 0.1f); break;
                }
            }
            else
            {
                int index = (int)style;
                if (index >= 0 && index < s_impactStyleNames.Length) _impactOccurred(s_impactStyleNames[index]);
            }
#endif
        }

        private void DispatchNotification(IOSNotificationStyle style)
        {
#if UNITY_IOS
            if (Capability.SupportsCoreHaptics)
            {
                switch (style)
                {
                    case IOSNotificationStyle.Success: _CoreHapticsPlayTransient(0.7f, 0.6f); break;
                    case IOSNotificationStyle.Warning: _CoreHapticsPlayTransient(0.5f, 0.4f); break;
                    case IOSNotificationStyle.Error: _CoreHapticsPlayTransient(0.8f, 0.9f); break;
                }
            }
            else
            {
                int index = (int)style;
                if (index >= 0 && index < s_notifStyleNames.Length) _notificationOccurred(s_notifStyleNames[index]);
            }
#endif
        }

        private void DispatchSelection()
        {
#if UNITY_IOS
            if (Capability.SupportsCoreHaptics) _CoreHapticsPlayTransient(0.3f, 0.9f);
            else _selectionChanged();
#endif
        }

        private void StopNative()
        {
#if UNITY_ANDROID
            if (_vibrator != null && Application.platform == RuntimePlatform.Android) _vibrator.Call("cancel");
#elif UNITY_IOS
            if (_coreHapticsAvailable) _CoreHapticsStop();
#elif UNITY_WEBGL
            VibrateWebgl(0);
#endif
        }

        private DeviceCapability DetectCapability()
        {
#if UNITY_ANDROID
            if (Application.platform != RuntimePlatform.Android) return DeviceCapability.None;
            bool has = _vibrator != null && _vibrator.Call<bool>("hasVibrator");
            int sdk = SdkVersion;
            return new DeviceCapability(has, has && sdk >= 26, false, false, has, false, sdk);
#elif UNITY_IOS
            if (Application.platform != RuntimePlatform.IPhonePlayer) return DeviceCapability.None;
            bool core = _CoreHapticsSupported();
            bool fallback = _HasVibrator();
            bool has = core || fallback;
            return new DeviceCapability(has, core, core, core, core, core, 0);
#elif UNITY_WEBGL
            if (Application.platform != RuntimePlatform.WebGLPlayer) return DeviceCapability.None;
            return new DeviceCapability(true, false, false, false, false, false, 0);
#else
            return DeviceCapability.None;
#endif
        }

        private static bool IsDefined(HapticPreset preset)
        {
            return preset >= HapticPreset.Light && preset <= HapticPreset.Selection;
        }

        private static bool IsDefinedImpact(IOSImpactStyle style)
        {
            return style >= IOSImpactStyle.Heavy && style <= IOSImpactStyle.Soft;
        }

        private static bool IsDefinedNotification(IOSNotificationStyle style)
        {
            return style >= IOSNotificationStyle.Error && style <= IOSNotificationStyle.Warning;
        }

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
                    else _sdkVersion = 0;
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
            else _vibrator.Call("vibrate", milliseconds);
        }

        private void VibrateAndroidPattern(long[] pattern, int repeat)
        {
            if (_vibrator == null) return;
            if (SdkVersion >= 26)
            {
                using (var effect = _vibrationEffect.CallStatic<AndroidJavaObject>("createWaveform", pattern, repeat))
                    _vibrator.Call("vibrate", effect);
            }
            else _vibrator.Call("vibrate", pattern, repeat);
        }

        private void PlayAndroidWaveform(int segmentCount)
        {
            if (_vibrator == null || segmentCount < 1) return;

            if (SdkVersion >= 26)
            {
                _buffers.GetExactArrays(segmentCount, out long[] timings, out int[] amplitudes, out long[] _);
                Array.Copy(_buffers.Timing, timings, segmentCount);
                Array.Copy(_buffers.Amplitude, amplitudes, segmentCount);
                using (var effect = _vibrationEffect.CallStatic<AndroidJavaObject>(
                    "createWaveform", timings, amplitudes, -1))
                {
                    _vibrator.Call("vibrate", effect);
                }
            }
            else
            {
                int idx = CollapseToOnOff(segmentCount);
                // The array handed to JNI has Length == idx.
                _buffers.GetExactArrays(idx, out long[] _, out int[] _, out long[] exactOnOff);
                Array.Copy(_buffers.Build, exactOnOff, idx);
                _vibrator.Call("vibrate", exactOnOff, -1);
            }
        }

        /// <summary>
        /// Collapses an amplitude timeline into binary on/off segments.
        /// Writes into the lease-owned Build array; the caller copies exactly
        /// idx entries into an exact-length array before native dispatch.
        /// </summary>
        private int CollapseToOnOff(int segmentCount)
        {
            const int threshold = 20;
            int idx = 0;
            long pause = 0L;
            long vibrate = 0L;
            for (int i = 0; i < segmentCount; i++)
            {
                if (_buffers.Amplitude[i] > threshold)
                {
                    if (vibrate == 0L)
                    {
                        _buffers.Build[idx++] = pause;
                        pause = 0L;
                    }
                    vibrate += _buffers.Timing[i];
                }
                else
                {
                    if (vibrate > 0L)
                    {
                        _buffers.Build[idx++] = vibrate;
                        vibrate = 0L;
                    }
                    pause += _buffers.Timing[i];
                }
            }
            if (vibrate > 0L)
            {
                if (idx == 0) _buffers.Build[idx++] = 0L;
                _buffers.Build[idx++] = vibrate;
            }
            if (idx == 0)
            {
                _buffers.Build[idx++] = 0L;
                _buffers.Build[idx++] = 1L;
            }
            return idx;
        }

        private bool TryPlayAndroidPrimitive(HapticPreset preset)
        {
            int primitiveId;
            float scale;
            switch (preset)
            {
                case HapticPreset.Light: primitiveId = 7; scale = 0.4f; break;
                case HapticPreset.Medium: primitiveId = 1; scale = 0.6f; break;
                case HapticPreset.Heavy: primitiveId = 3; scale = 1f; break;
                case HapticPreset.Selection: primitiveId = 8; scale = 0.3f; break;
                case HapticPreset.Success: primitiveId = 1; scale = 0.8f; break;
                case HapticPreset.Warning: primitiveId = 1; scale = 0.5f; break;
                case HapticPreset.Error: primitiveId = 3; scale = 0.7f; break;
                default: return false;
            }

            try
            {
                using (var composition = _vibrationEffect.CallStatic<AndroidJavaObject>("startComposition"))
                {
                    using (var primitive = composition.Call<AndroidJavaObject>("addPrimitive", primitiveId, scale, 0))
                    {
                        using (var effect = composition.Call<AndroidJavaObject>("compose"))
                            _vibrator.Call("vibrate", effect);
                    }
                }
                return true;
            }
            catch (Exception)
            {
                // Record the platform failure and allow RefreshCapabilities to retry.
                _compositionFailureCount++;
                if (_compositionFailureCount >= CompositionFailureThreshold) _compositionDegraded = true;
                return false;
            }
        }
#endif

#if UNITY_IOS
        private static void MapIntensityToImpact(float intensity)
        {
            if (intensity < 0.33f) _impactOccurred("Light");
            else if (intensity < 0.66f) _impactOccurred("Medium");
            else _impactOccurred("Heavy");
        }
#endif

        private static int ClampToInt(long value) => (int)Math.Min(value, int.MaxValue);

        private DeviceFeedbackAdmissionFailure ValidatePattern(long[] pattern, int repeat,
                                                                out long totalDurationMilliseconds)
        {
            totalDurationMilliseconds = 0L;
            if (pattern == null) return DeviceFeedbackAdmissionFailure.Invalid;
            if (pattern.Length == 0 || repeat < -1 || repeat >= pattern.Length)
            {
                return DeviceFeedbackAdmissionFailure.Invalid;
            }
            if (pattern.Length > _limits.MaximumPatternSegmentCount)
            {
                return DeviceFeedbackAdmissionFailure.Capacity;
            }
            for (int index = 0; index < pattern.Length; index++)
            {
                long segment = pattern[index];
                if (segment < 0L) return DeviceFeedbackAdmissionFailure.Invalid;
                if (segment > _limits.MaximumDurationMilliseconds - totalDurationMilliseconds)
                {
                    return DeviceFeedbackAdmissionFailure.Capacity;
                }
                totalDurationMilliseconds += segment;
            }
            return totalDurationMilliseconds > 0L
                ? DeviceFeedbackAdmissionFailure.None
                : DeviceFeedbackAdmissionFailure.Invalid;
        }

        #endregion
    }
}
