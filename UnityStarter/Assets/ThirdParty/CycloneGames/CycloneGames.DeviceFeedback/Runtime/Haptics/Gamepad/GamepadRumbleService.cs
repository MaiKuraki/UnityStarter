using UnityEngine;

namespace CycloneGames.DeviceFeedback.Runtime
{
    /// <summary>
    /// High-level gamepad rumble service. The default constructor is an explicit no-op;
    /// install a platform backend to drive real controller hardware.
    /// </summary>
    public sealed class GamepadRumbleService : IGamepadRumbleService
    {
        private readonly IGamepadRumbleBackend _backend;
        private readonly bool _ownsBackend;
        private bool _isActive = true;
        private bool _disposed;

        public GamepadRumbleService()
            : this(NoopGamepadRumbleBackend.Instance, false)
        {
        }

        public GamepadRumbleService(IGamepadRumbleBackend backend, bool ownsBackend = true)
        {
            _backend = backend ?? NoopGamepadRumbleBackend.Instance;
            _ownsBackend = ownsBackend && !ReferenceEquals(_backend, NoopGamepadRumbleBackend.Instance);
        }

        public bool IsAvailable => !_disposed && _backend.IsAvailable;

        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (_isActive == value)
                {
                    return;
                }

                _isActive = value;
                if (!_isActive)
                {
                    Cancel();
                }
            }
        }

        public void Initialize()
        {
            if (_disposed)
            {
                return;
            }

            _backend.Initialize();
        }

        public void PlayPreset(HapticPreset preset)
        {
            switch (preset)
            {
                case HapticPreset.Light:
                    Play(0.25f, 0.05f, 0.8f);
                    break;
                case HapticPreset.Medium:
                    Play(0.5f, 0.08f, 0.5f);
                    break;
                case HapticPreset.Heavy:
                    Play(1f, 0.12f, 0.25f);
                    break;
                case HapticPreset.Success:
                    Play(0.65f, 0.08f, 0.7f);
                    break;
                case HapticPreset.Warning:
                    Play(0.55f, 0.1f, 0.4f);
                    break;
                case HapticPreset.Error:
                    Play(0.9f, 0.16f, 0.9f);
                    break;
                case HapticPreset.Selection:
                    Play(0.2f, 0.03f, 1f);
                    break;
            }
        }

        public void Play(float normalizedIntensity, float durationSeconds, float sharpness = 0.5f)
        {
            if (!CanOperate() || durationSeconds <= 0f)
            {
                return;
            }

            normalizedIntensity = Mathf.Clamp01(normalizedIntensity);
            sharpness = Mathf.Clamp01(sharpness);
            if (normalizedIntensity <= 0f)
            {
                Cancel();
                return;
            }

            CalculateMotorSpeeds(normalizedIntensity, sharpness, out float lowFrequency, out float highFrequency);
            _backend.Rumble(lowFrequency, highFrequency, durationSeconds);
        }

        public void PlayCurve(AnimationCurve intensityCurve, float durationSeconds,
                              AnimationCurve sharpnessCurve = null, int sampleIntervalMs = 20)
        {
            if (intensityCurve == null || durationSeconds <= 0f || !CanOperate())
            {
                return;
            }

            sampleIntervalMs = Mathf.Max(sampleIntervalMs, 10);
            int sampleCount = Mathf.Max(1, Mathf.CeilToInt(durationSeconds * 1000f / sampleIntervalMs));
            float peakIntensity = 0f;
            float sharpnessAtPeak = 0.5f;

            for (int i = 0; i < sampleCount; i++)
            {
                float t = sampleCount == 1 ? 1f : (float)i / (sampleCount - 1);
                float intensity = Mathf.Clamp01(intensityCurve.Evaluate(t));
                if (intensity > peakIntensity)
                {
                    peakIntensity = intensity;
                    sharpnessAtPeak = sharpnessCurve != null ? Mathf.Clamp01(sharpnessCurve.Evaluate(t)) : 0.5f;
                }
            }

            Play(peakIntensity, durationSeconds, sharpnessAtPeak);
        }

        public void PlayClip(HapticClip clip)
        {
            if (clip == null || !CanOperate())
            {
                return;
            }

            if (!clip.HasEvents)
            {
                PlayCurve(clip.intensityCurve, clip.duration, clip.sharpnessCurve);
                return;
            }

            HapticEvent[] events = clip.events;
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

            Play(peakIntensity, durationAtPeak, sharpnessAtPeak);
        }

        public void Cancel()
        {
            if (_disposed)
            {
                return;
            }

            _backend.Stop();
        }

        public void SetMotorSpeeds(float lowFrequency, float highFrequency)
        {
            if (!CanOperate())
            {
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
        }

        public void Rumble(float lowFrequency, float highFrequency, float durationSeconds)
        {
            if (!CanOperate() || durationSeconds <= 0f)
            {
                return;
            }

            lowFrequency = Mathf.Clamp01(lowFrequency);
            highFrequency = Mathf.Clamp01(highFrequency);
            if (lowFrequency <= 0f && highFrequency <= 0f)
            {
                Cancel();
                return;
            }

            _backend.Rumble(lowFrequency, highFrequency, durationSeconds);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Cancel();
            _disposed = true;
            if (_ownsBackend)
            {
                _backend.Dispose();
            }
        }

        private bool CanOperate()
        {
            return !_disposed && _isActive && _backend.IsAvailable;
        }

        private static void CalculateMotorSpeeds(float normalizedIntensity, float sharpness, out float lowFrequency, out float highFrequency)
        {
            lowFrequency = Mathf.Clamp01(normalizedIntensity * (1f - sharpness * 0.5f));
            highFrequency = Mathf.Clamp01(normalizedIntensity * (0.5f + sharpness * 0.5f));
        }
    }
}
