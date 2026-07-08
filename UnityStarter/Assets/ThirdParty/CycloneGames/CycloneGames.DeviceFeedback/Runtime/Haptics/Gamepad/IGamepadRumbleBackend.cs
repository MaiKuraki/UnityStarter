using System;
namespace CycloneGames.DeviceFeedback.Runtime
{
    /// <summary>
    /// Low-level gamepad rumble adapter. Platform integrations own device discovery,
    /// timing, player loop hooks, and native API calls.
    /// </summary>
    public interface IGamepadRumbleBackend : IDisposable
    {
        bool IsAvailable { get; }

        void Initialize();
        void SetMotorSpeeds(float lowFrequency, float highFrequency);
        void Rumble(float lowFrequency, float highFrequency, float durationSeconds);
        void Stop();
    }
}
