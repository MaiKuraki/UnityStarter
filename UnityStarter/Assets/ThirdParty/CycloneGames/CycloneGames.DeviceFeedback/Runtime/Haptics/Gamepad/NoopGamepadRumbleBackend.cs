namespace CycloneGames.DeviceFeedback.Runtime
{
    /// <summary>
    /// Explicit no-op backend used when no platform rumble integration is installed.
    /// </summary>
    public sealed class NoopGamepadRumbleBackend : IGamepadRumbleBackend
    {
        public static readonly NoopGamepadRumbleBackend Instance = new NoopGamepadRumbleBackend();

        private NoopGamepadRumbleBackend()
        {
        }

        public bool IsAvailable => false;

        public void Initialize()
        {
        }

        public void SetMotorSpeeds(float lowFrequency, float highFrequency)
        {
        }

        public void Rumble(float lowFrequency, float highFrequency, float durationSeconds)
        {
        }

        public void Stop()
        {
        }

        public void Dispose()
        {
        }
    }
}
