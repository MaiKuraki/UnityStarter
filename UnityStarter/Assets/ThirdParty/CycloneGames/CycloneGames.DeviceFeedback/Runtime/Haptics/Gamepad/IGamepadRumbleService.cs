using System;

namespace CycloneGames.DeviceFeedback.Runtime
{
    /// <summary>
    /// Contract for gamepad/controller rumble motors.
    /// Inherits universal haptic operations from <see cref="IHapticFeedbackService"/>.
    /// </summary>
    public interface IGamepadRumbleService : IHapticFeedbackService, IDisposable
    {
        /// <summary>
        /// Set dual-motor rumble speeds directly.
        /// </summary>
        /// <param name="lowFrequency">Left (heavy) motor, 0.0 ~ 1.0.</param>
        /// <param name="highFrequency">Right (light) motor, 0.0 ~ 1.0.</param>
        void SetMotorSpeeds(float lowFrequency, float highFrequency);

        /// <summary>
        /// Rumble with dual motors for a specified duration, then auto-stop.
        /// </summary>
        void Rumble(float lowFrequency, float highFrequency, float durationSeconds);
    }
}
