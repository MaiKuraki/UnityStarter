using UnityEngine;

namespace CycloneGames.DeviceFeedback.Runtime
{
    /// <summary>
    /// Placeholder for gamepad light bar control (e.g. DualSense, DualShock 4).
    /// TODO: Implement using UnityEngine.InputSystem.DualShock or platform-specific APIs.
    /// </summary>
    public sealed class GamepadLightService : IDeviceLightService
    {
        private bool _disposed;

        public bool IsAvailable => false; // TODO: detect gamepad with light bar
        public bool IsActive { get; set; } = true;

        public void Initialize()
        {
            // TODO: detect DualShock/DualSense controller
        }

        public void SetColor(Color color)
        {
            // TODO: DualShockGamepad.current?.SetLightBarColor(color)
        }

        public void Flash(Color onColor, Color offColor, float onDurationSeconds, float offDurationSeconds)
        {
            // TODO: implement timed color alternation
        }

        public void PlayGradient(Gradient gradient, float durationSeconds, int sampleIntervalMs = 50)
        {
            // TODO: sample gradient per frame, call SetLightBarColor at each interval
            // Needs MonoBehaviour or PlayerLoopSystem hook for per-frame updates
        }

        public void PlayIntensityCurve(Color baseColor, AnimationCurve intensityCurve, float durationSeconds, int sampleIntervalMs = 50)
        {
            // TODO: evaluate curve per frame → baseColor * curveValue → SetLightBarColor
        }

        public void CancelAnimation()
        {
            // TODO: stop running gradient/intensity animation
        }

        public void Reset()
        {
            // TODO: restore default light bar color
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Reset();
        }
    }
}
