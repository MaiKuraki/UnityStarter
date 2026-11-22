using System;
using System.Threading;
using UnityEngine;

namespace CycloneGames.DeviceFeedback.Runtime
{
    /// <summary>
    /// High-level device light service. The default constructor is an explicit
    /// no-op; install a platform backend to drive real hardware. Every method
    /// uses the same admission path and result contract as the haptic services.
    /// </summary>
    public sealed class GamepadLightService : IDeviceLightService, IFeedbackResultProvider
    {
        private readonly IDeviceLightBackend _backend;
        private readonly bool _ownsBackend;
        private readonly DeviceFeedbackLimits _limits;
        private readonly DeviceFeedbackProfile _profile;
        private readonly DeviceFeedbackCounters _counters = new DeviceFeedbackCounters();
        private FeedbackResult _lastResult;
        private bool _isActive = true;
        private bool _disposed;
        private int _ownerThreadId;

        public GamepadLightService()
            : this(NoopDeviceLightBackend.Instance, DeviceFeedbackLimits.Default, false)
        {
        }

        public GamepadLightService(IDeviceLightBackend backend, bool ownsBackend = true)
            : this(backend, DeviceFeedbackLimits.Default, ownsBackend)
        {
        }

        public GamepadLightService(IDeviceLightBackend backend, DeviceFeedbackLimits limits, bool ownsBackend = true)
            : this(backend, limits, DeviceFeedbackProfile.Default, ownsBackend)
        {
        }

        public GamepadLightService(IDeviceLightBackend backend, DeviceFeedbackLimits limits,
                                   DeviceFeedbackProfile profile, bool ownsBackend = true)
        {
            _backend = backend ?? NoopDeviceLightBackend.Instance;
            _ownsBackend = ownsBackend && !ReferenceEquals(_backend, NoopDeviceLightBackend.Instance);
            _limits = limits.Normalize();
            _profile = profile;
        }

        public bool IsAvailable => !_disposed && _backend.IsAvailable;
        public DeviceFeedbackLimits Limits => _limits;
        public FeedbackResult LastResult => _lastResult;
        public DeviceCapability Capability => new DeviceCapability(
            !_disposed && _backend.IsAvailable, true, false, false, false, false, 0);

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
                if (!_isActive) Reset();
            }
        }

        public void Initialize()
        {
            if (_disposed) { Finish(FeedbackResult.Rejected(FeedbackStatus.RejectedDisposed)); return; }
            _backend.Initialize();
            Finish(FeedbackResult.Accepted(0L));
        }

        public void SetColor(Color color)
        {
            FeedbackStatus guard = Guard();
            if (guard != FeedbackStatus.Accepted) { Finish(FeedbackResult.Rejected(guard)); return; }
            if (!IsFiniteColor(color))
            {
                Finish(FeedbackResult.Rejected(FeedbackStatus.RejectedInvalid));
                return;
            }
            _backend.SetColor(ClampColor(color));
            Finish(FeedbackResult.Accepted(0L));
        }

        public void Flash(Color onColor, Color offColor, float onDurationSeconds, float offDurationSeconds)
        {
            FeedbackStatus guard = Guard();
            if (guard != FeedbackStatus.Accepted) { Finish(FeedbackResult.Rejected(guard)); return; }
            if (!IsFiniteColor(onColor) || !IsFiniteColor(offColor))
            {
                Finish(FeedbackResult.Rejected(FeedbackStatus.RejectedInvalid));
                return;
            }

            DeviceFeedbackAdmissionFailure onFailure = DeviceFeedbackAdmission.ValidateDurationSeconds(
                onDurationSeconds, in _limits, out long onMilliseconds);
            DeviceFeedbackAdmissionFailure offFailure = DeviceFeedbackAdmission.ValidateDurationSeconds(
                offDurationSeconds, in _limits, out long offMilliseconds);
            if (onFailure != DeviceFeedbackAdmissionFailure.None)
            {
                Finish(FeedbackResult.Rejected(DeviceFeedbackAdmission.ToStatus(onFailure)));
                return;
            }
            if (offFailure != DeviceFeedbackAdmissionFailure.None)
            {
                Finish(FeedbackResult.Rejected(DeviceFeedbackAdmission.ToStatus(offFailure)));
                return;
            }

            long total = onMilliseconds + offMilliseconds;
            if (total > _limits.MaximumDurationMilliseconds)
            {
                Finish(FeedbackResult.Rejected(FeedbackStatus.RejectedCapacity));
                return;
            }

            _backend.Flash(ClampColor(onColor), ClampColor(offColor), onDurationSeconds, offDurationSeconds);
            Finish(FeedbackResult.Accepted(total));
        }

        public void PlayGradient(Gradient gradient, float durationSeconds, int sampleIntervalMs = 50)
        {
            FeedbackStatus guard = Guard();
            if (guard != FeedbackStatus.Accepted) { Finish(FeedbackResult.Rejected(guard)); return; }
            if (gradient == null)
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
            _backend.PlayGradient(gradient, durationSeconds, interval);
            Finish(FeedbackResult.Accepted(durationMilliseconds, sampleCount));
        }

        public void PlayIntensityCurve(Color baseColor, AnimationCurve intensityCurve,
                                       float durationSeconds, int sampleIntervalMs = 50)
        {
            FeedbackStatus guard = Guard();
            if (guard != FeedbackStatus.Accepted) { Finish(FeedbackResult.Rejected(guard)); return; }
            if (intensityCurve == null || !IsFiniteColor(baseColor))
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
            _backend.PlayIntensityCurve(ClampColor(baseColor), intensityCurve, durationSeconds, interval);
            Finish(FeedbackResult.Accepted(durationMilliseconds, sampleCount));
        }

        public void CancelAnimation()
        {
            if (_disposed) { Finish(FeedbackResult.Rejected(FeedbackStatus.RejectedDisposed)); return; }
            _backend.CancelAnimation();
            Finish(FeedbackResult.Accepted(0L));
        }

        public void Reset()
        {
            if (_disposed) { Finish(FeedbackResult.Rejected(FeedbackStatus.RejectedDisposed)); return; }
            _backend.CancelAnimation();
            _backend.Reset();
            Finish(FeedbackResult.Accepted(0L));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _backend.CancelAnimation();
            _backend.Reset();
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

        private static bool IsFiniteColor(Color color)
        {
            return DeviceFeedbackAdmission.IsFinite(color.r) &&
                   DeviceFeedbackAdmission.IsFinite(color.g) &&
                   DeviceFeedbackAdmission.IsFinite(color.b) &&
                   DeviceFeedbackAdmission.IsFinite(color.a);
        }

        private static Color ClampColor(Color color)
        {
            color.r = Mathf.Clamp01(color.r);
            color.g = Mathf.Clamp01(color.g);
            color.b = Mathf.Clamp01(color.b);
            color.a = Mathf.Clamp01(color.a);
            return color;
        }
    }
}
