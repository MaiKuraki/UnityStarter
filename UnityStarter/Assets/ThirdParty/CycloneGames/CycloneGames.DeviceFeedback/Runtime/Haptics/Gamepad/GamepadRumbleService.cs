using System;
using System.Threading;
using UnityEngine;

namespace CycloneGames.DeviceFeedback.Runtime
{
    /// <summary>
    /// Gamepad rumble service. The default constructor uses a no-op backend;
    /// inject a platform backend to drive controller hardware.
    /// </summary>
    public sealed class GamepadRumbleService : IGamepadRumbleService, IFeedbackResultProvider
    {
        private readonly IGamepadRumbleBackend _backend;
        private readonly bool _ownsBackend;
        private readonly DeviceFeedbackLimits _limits;
        private readonly DeviceFeedbackProfile _profile;
        private readonly DeviceFeedbackCounters _counters = new DeviceFeedbackCounters();
        private FeedbackResult _lastResult;
        private bool _isActive = true;
        private bool _disposed;
        private int _ownerThreadId;

        public GamepadRumbleService()
            : this(NoopGamepadRumbleBackend.Instance, DeviceFeedbackLimits.Default, false)
        {
        }

        public GamepadRumbleService(IGamepadRumbleBackend backend, bool ownsBackend = true)
            : this(backend, DeviceFeedbackLimits.Default, ownsBackend)
        {
        }

        public GamepadRumbleService(IGamepadRumbleBackend backend, DeviceFeedbackLimits limits, bool ownsBackend = true)
            : this(backend, limits, DeviceFeedbackProfile.Default, ownsBackend)
        {
        }

        public GamepadRumbleService(IGamepadRumbleBackend backend, DeviceFeedbackLimits limits,
                                    DeviceFeedbackProfile profile, bool ownsBackend = true)
        {
            _backend = backend ?? NoopGamepadRumbleBackend.Instance;
            _ownsBackend = ownsBackend && !ReferenceEquals(_backend, NoopGamepadRumbleBackend.Instance);
            _limits = limits.Normalize();
            _profile = profile;
        }

        public bool IsAvailable => !_disposed && _backend.IsAvailable;
        public DeviceFeedbackLimits Limits => _limits;
        public FeedbackResult LastResult => _lastResult;
        public DeviceCapability Capability => new DeviceCapability(
            !_disposed && _backend.IsAvailable, true, true, false, false, false, 0);

        public DeviceFeedbackMemoryStats GetMemoryStats()
        {
            return _counters.GetStats(DeviceFeedbackBufferStats.Empty);
        }

        public void ResetDiagnostics() => _counters.Reset();
        public void RefreshCapabilities() => _backend.Initialize();

        public void Reinitialize()
        {
            _disposed = false;
            Volatile.Write(ref _ownerThreadId, 0);
            _backend.Initialize();
        }

        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (_isActive == value) return;
                _isActive = value;
                if (!_isActive) Cancel();
            }
        }

        public void Initialize()
        {
            if (_disposed) { Finish(FeedbackResult.Rejected(FeedbackStatus.RejectedDisposed)); return; }
            _backend.Initialize();
            Finish(FeedbackResult.Accepted(0L));
        }

        public void PlayPreset(HapticPreset preset)
        {
            FeedbackStatus guard = Guard();
            if (guard != FeedbackStatus.Accepted) { Finish(FeedbackResult.Rejected(guard)); return; }
            if (!IsDefined(preset)) { Finish(FeedbackResult.Rejected(FeedbackStatus.RejectedInvalid)); return; }

            switch (preset)
            {
                case HapticPreset.Light: Play(0.25f, 0.05f, 0.8f); break;
                case HapticPreset.Medium: Play(0.5f, 0.08f, 0.5f); break;
                case HapticPreset.Heavy: Play(1f, 0.12f, 0.25f); break;
                case HapticPreset.Success: Play(0.65f, 0.08f, 0.7f); break;
                case HapticPreset.Warning: Play(0.55f, 0.1f, 0.4f); break;
                case HapticPreset.Error: Play(0.9f, 0.16f, 0.9f); break;
                case HapticPreset.Selection: Play(0.2f, 0.03f, 1f); break;
            }
        }

        public void Play(float normalizedIntensity, float durationSeconds, float sharpness = 0.5f)
        {
            FeedbackStatus guard = Guard();
            if (guard != FeedbackStatus.Accepted) { Finish(FeedbackResult.Rejected(guard)); return; }
            if (!DeviceFeedbackAdmission.IsFinite(normalizedIntensity) ||
                !DeviceFeedbackAdmission.IsFinite(sharpness))
            {
                Finish(FeedbackResult.Rejected(FeedbackStatus.RejectedInvalid));
                return;
            }

            float intensity = Mathf.Clamp01(normalizedIntensity);
            float crispness = Mathf.Clamp01(sharpness);
            if (intensity <= 0f)
            {
                Cancel();
                Finish(FeedbackResult.Rejected(FeedbackStatus.RejectedInvalid));
                return;
            }

            DeviceFeedbackAdmissionFailure failure = DeviceFeedbackAdmission.ValidateDurationSeconds(
                durationSeconds, in _limits, out long durationMilliseconds);
            if (failure != DeviceFeedbackAdmissionFailure.None)
            {
                Finish(FeedbackResult.Rejected(DeviceFeedbackAdmission.ToStatus(failure)));
                return;
            }

            CalculateMotorSpeeds(intensity, crispness, out float low, out float high);
            _backend.Rumble(low, high, durationSeconds);
            Finish(FeedbackResult.Accepted(durationMilliseconds));
        }

        public void PlayCurve(AnimationCurve intensityCurve, float durationSeconds,
                              AnimationCurve sharpnessCurve = null, int sampleIntervalMs = 20)
        {
            FeedbackStatus guard = Guard();
            if (guard != FeedbackStatus.Accepted) { Finish(FeedbackResult.Rejected(guard)); return; }
            if (intensityCurve == null)
            {
                Finish(FeedbackResult.Rejected(FeedbackStatus.RejectedInvalid));
                return;
            }

            int interval = DeviceFeedbackAdmission.SanitizeSampleInterval(sampleIntervalMs, in _profile);
            DeviceFeedbackAdmissionFailure failure = DeviceFeedbackAdmission.CalculateSampleCount(
                durationSeconds, interval, in _limits, in _profile,
                out long durationMilliseconds, out int sampleCount);
            if (failure != DeviceFeedbackAdmissionFailure.None)
            {
                Finish(FeedbackResult.Rejected(DeviceFeedbackAdmission.ToStatus(failure)));
                return;
            }

            float peakIntensity = 0f;
            float sharpnessAtPeak = 0.5f;
            for (int i = 0; i < sampleCount; i++)
            {
                float t = sampleCount == 1 ? 1f : (float)i / (sampleCount - 1);
                float intensity = Mathf.Clamp01(intensityCurve.Evaluate(t));
                if (!DeviceFeedbackAdmission.IsFinite(intensity)) continue;
                if (intensity > peakIntensity)
                {
                    peakIntensity = intensity;
                    float sharp = sharpnessCurve != null ? Mathf.Clamp01(sharpnessCurve.Evaluate(t)) : 0.5f;
                    sharpnessAtPeak = DeviceFeedbackAdmission.IsFinite(sharp) ? sharp : 0.5f;
                }
            }

            if (peakIntensity > 0f)
            {
                CalculateMotorSpeeds(peakIntensity, sharpnessAtPeak, out float low, out float high);
                _backend.Rumble(low, high, durationSeconds);
            }
            Finish(FeedbackResult.Accepted(durationMilliseconds, sampleCount));
        }

        public void PlayClip(HapticClip clip)
        {
            FeedbackStatus guard = Guard();
            if (guard != FeedbackStatus.Accepted) { Finish(FeedbackResult.Rejected(guard)); return; }
            if (clip == null)
            {
                Finish(FeedbackResult.Rejected(FeedbackStatus.RejectedInvalid));
                return;
            }

            if (!clip.HasEvents)
            {
                PlayCurve(clip.intensityCurve, clip.duration, clip.sharpnessCurve);
                return;
            }

            HapticEvent[] events = clip.events;
            DeviceFeedbackAdmissionFailure failure = DeviceFeedbackAdmission.ValidateEvents(
                events, in _limits, false, out long durationMilliseconds);
            if (failure != DeviceFeedbackAdmissionFailure.None)
            {
                Finish(FeedbackResult.Rejected(DeviceFeedbackAdmission.ToStatus(failure)));
                return;
            }

            float peakIntensity = 0f;
            float sharpnessAtPeak = 0.5f;
            float durationAtPeak = 0.03f;
            for (int i = 0; i < events.Length; i++)
            {
                float intensity = Mathf.Clamp01(events[i].intensity);
                if (intensity > peakIntensity)
                {
                    peakIntensity = intensity;
                    sharpnessAtPeak = Mathf.Clamp01(events[i].sharpness);
                    durationAtPeak = events[i].type == HapticEventType.Continuous
                        ? Mathf.Max(events[i].duration, 0.01f)
                        : 0.03f;
                }
            }

            if (peakIntensity > 0f)
            {
                CalculateMotorSpeeds(peakIntensity, sharpnessAtPeak, out float low, out float high);
                _backend.Rumble(low, high, durationAtPeak);
            }
            Finish(FeedbackResult.Accepted(durationMilliseconds, hapticEventCount: events.Length));
        }

        public void Cancel()
        {
            if (_disposed) { Finish(FeedbackResult.Rejected(FeedbackStatus.RejectedDisposed)); return; }
            _backend.Stop();
            Finish(FeedbackResult.Accepted(0L));
        }

        public void SetMotorSpeeds(float lowFrequency, float highFrequency)
        {
            FeedbackStatus guard = Guard();
            if (guard != FeedbackStatus.Accepted) { Finish(FeedbackResult.Rejected(guard)); return; }
            if (!DeviceFeedbackAdmission.IsFinite(lowFrequency) || !DeviceFeedbackAdmission.IsFinite(highFrequency))
            {
                Finish(FeedbackResult.Rejected(FeedbackStatus.RejectedInvalid));
                return;
            }
            lowFrequency = Mathf.Clamp01(lowFrequency);
            highFrequency = Mathf.Clamp01(highFrequency);
            if (lowFrequency <= 0f && highFrequency <= 0f)
            {
                Cancel();
                return;
            }
            _backend.SetMotorSpeeds(lowFrequency, highFrequency);
            Finish(FeedbackResult.Accepted(0L));
        }

        public void Rumble(float lowFrequency, float highFrequency, float durationSeconds)
        {
            FeedbackStatus guard = Guard();
            if (guard != FeedbackStatus.Accepted) { Finish(FeedbackResult.Rejected(guard)); return; }
            if (!DeviceFeedbackAdmission.IsFinite(lowFrequency) ||
                !DeviceFeedbackAdmission.IsFinite(highFrequency))
            {
                Finish(FeedbackResult.Rejected(FeedbackStatus.RejectedInvalid));
                return;
            }
            lowFrequency = Mathf.Clamp01(lowFrequency);
            highFrequency = Mathf.Clamp01(highFrequency);
            if (lowFrequency <= 0f && highFrequency <= 0f)
            {
                Cancel();
                return;
            }

            DeviceFeedbackAdmissionFailure failure = DeviceFeedbackAdmission.ValidateDurationSeconds(
                durationSeconds, in _limits, out long durationMilliseconds);
            if (failure != DeviceFeedbackAdmissionFailure.None)
            {
                Finish(FeedbackResult.Rejected(DeviceFeedbackAdmission.ToStatus(failure)));
                return;
            }
            _backend.Rumble(lowFrequency, highFrequency, durationSeconds);
            Finish(FeedbackResult.Accepted(durationMilliseconds));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _backend.Stop();
            _disposed = true;
            if (_ownsBackend) _backend.Dispose();
        }

        private FeedbackStatus Guard()
        {
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
            if (!_backend.IsAvailable) return FeedbackStatus.RejectedUnavailable;
            return FeedbackStatus.Accepted;
        }

        private void Finish(FeedbackResult result)
        {
            if (result.Status == FeedbackStatus.RejectedThreadAffinity)
            {
                DeviceFeedbackDiagnostics.RecordRejected(result.Status);
                return;
            }
            _lastResult = result;
            if (result.IsRejected)
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
        }

        private static bool IsDefined(HapticPreset preset)
        {
            return preset >= HapticPreset.Light && preset <= HapticPreset.Selection;
        }

        private static void CalculateMotorSpeeds(float normalizedIntensity, float sharpness,
                                                  out float lowFrequency, out float highFrequency)
        {
            lowFrequency = Mathf.Clamp01(normalizedIntensity * (1f - sharpness * 0.5f));
            highFrequency = Mathf.Clamp01(normalizedIntensity * (0.5f + sharpness * 0.5f));
        }
    }
}
