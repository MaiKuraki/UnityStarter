using UnityEngine;

namespace CycloneGames.DeviceFeedback.Runtime
{
    /// <summary>
    /// Placeholder for gamepad rumble implementation.
    /// TODO: Implement using UnityEngine.InputSystem.Gamepad or platform-specific APIs.
    /// </summary>
    public sealed class GamepadRumbleService : IGamepadRumbleService
    {
        private bool _disposed;

        public bool IsAvailable => false; // TODO: detect connected gamepad
        public bool IsActive { get; set; } = true;

        public void Initialize()
        {
            // TODO: subscribe to InputSystem device change events
        }

        public void PlayPreset(HapticPreset preset)
        {
            // TODO: map HapticPreset to rumble intensities/patterns
        }

        public void Play(float normalizedIntensity, float durationSeconds, float sharpness = 0.5f)
        {
            // TODO: map normalized intensity to dual-motor values, sharpness to high/low motor balance
        }

        public void PlayCurve(AnimationCurve intensityCurve, float durationSeconds,
                              AnimationCurve sharpnessCurve = null, int sampleIntervalMs = 20)
        {
            // TODO: sample curve and drive motors per frame via coroutine/update loop
        }

        public void PlayClip(HapticClip clip)
        {
            // TODO: convert clip events to motor speed segments
        }

        public void Cancel()
        {
            // TODO: SetMotorSpeeds(0, 0)
        }

        public void SetMotorSpeeds(float lowFrequency, float highFrequency)
        {
            // TODO: Gamepad.current?.SetMotorSpeeds(low, high)
        }

        public void Rumble(float lowFrequency, float highFrequency, float durationSeconds)
        {
            // TODO: start coroutine or timer to auto-cancel after duration
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Cancel();
        }
    }
}
