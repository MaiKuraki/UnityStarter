namespace CycloneGames.Service
{
    /// <summary>
    /// Defines a generic contract for a service that manages a specific type of settings.
    /// This interface is compatible with C# 9.0 and below.
    /// </summary>
    /// <typeparam name="T">The type of the settings data, which must be a struct.</typeparam>
    public interface ISettingsService<T> where T : struct
    {
        /// <summary>
        /// Returns a direct reference to the settings data managed by the service.
        /// </summary>
        /// <returns>A reference to the internal settings struct.</returns>
        T Settings { get; }

        /// <summary>
        /// Saves the current state of the settings to its persistent storage.
        /// </summary>
        void SaveSettings();

        /// <summary>
        /// Loads the settings from persistent storage.
        /// </summary>
        void LoadSettings();

        bool IsInitialized { get; }
    }
}
