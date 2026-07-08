using UnityEngine;

namespace CycloneGames.DeviceFeedback.Runtime
{
    /// <summary>
    /// High-level gamepad light service. The default constructor is an explicit no-op;
    /// install a platform backend to drive real controller hardware.
    /// </summary>
    public sealed class GamepadLightService : IDeviceLightService
    {
        private readonly IDeviceLightBackend _backend;
        private readonly bool _ownsBackend;
        private bool _isActive = true;
        private bool _disposed;

        public GamepadLightService()
            : this(NoopDeviceLightBackend.Instance, false)
        {
        }

        public GamepadLightService(IDeviceLightBackend backend, bool ownsBackend = true)
        {
            _backend = backend ?? NoopDeviceLightBackend.Instance;
            _ownsBackend = ownsBackend && !ReferenceEquals(_backend, NoopDeviceLightBackend.Instance);
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
                    Reset();
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

        public void SetColor(Color color)
        {
            if (!CanOperate())
            {
                return;
            }

            _backend.SetColor(ClampColor(color));
        }

        public void Flash(Color onColor, Color offColor, float onDurationSeconds, float offDurationSeconds)
        {
            if (!CanOperate() || onDurationSeconds <= 0f || offDurationSeconds <= 0f)
            {
                return;
            }

            _backend.Flash(ClampColor(onColor), ClampColor(offColor), onDurationSeconds, offDurationSeconds);
        }

        public void PlayGradient(Gradient gradient, float durationSeconds, int sampleIntervalMs = 50)
        {
            if (gradient == null || !CanOperate() || durationSeconds <= 0f)
            {
                return;
            }

            _backend.PlayGradient(gradient, durationSeconds, SanitizeSampleInterval(sampleIntervalMs));
        }

        public void PlayIntensityCurve(Color baseColor, AnimationCurve intensityCurve, float durationSeconds, int sampleIntervalMs = 50)
        {
            if (intensityCurve == null || !CanOperate() || durationSeconds <= 0f)
            {
                return;
            }

            _backend.PlayIntensityCurve(ClampColor(baseColor), intensityCurve, durationSeconds, SanitizeSampleInterval(sampleIntervalMs));
        }

        public void CancelAnimation()
        {
            if (_disposed)
            {
                return;
            }

            _backend.CancelAnimation();
        }

        public void Reset()
        {
            if (_disposed)
            {
                return;
            }

            _backend.CancelAnimation();
            _backend.Reset();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Reset();
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

        private static int SanitizeSampleInterval(int sampleIntervalMs)
        {
            return Mathf.Max(sampleIntervalMs, 10);
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
