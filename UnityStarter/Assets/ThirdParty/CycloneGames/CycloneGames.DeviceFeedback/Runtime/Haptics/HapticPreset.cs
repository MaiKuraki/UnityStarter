namespace CycloneGames.DeviceFeedback.Runtime
{
    /// <summary>
    /// Universal haptic presets shared across all feedback devices
    /// (mobile vibration, gamepad rumble, etc.).
    /// Each device implementation maps these to its native capabilities.
    /// </summary>
    public enum HapticPreset
    {
        Light,
        Medium,
        Heavy,
        Success,
        Warning,
        Error,
        Selection
    }
}
