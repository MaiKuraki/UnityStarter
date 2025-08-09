using UnityEngine;

namespace CycloneGames.Logger
{
    /// <summary>
    /// Project-level configuration for CycloneGames.Logger.
    ///
    /// IMPORTANT: The default bootstrap loads this asset via Resources at
    /// Resources/CycloneGames.Logger/LoggerSettings (see <see cref="SettingsResourcePath"/>).
    /// Do not rename the asset or folder when using the default bootstrap path.
    /// </summary>
    [CreateAssetMenu(fileName = "LoggerSettings", menuName = "CycloneGames/Logger/LoggerSettings", order = 0)]
    public sealed class LoggerSettings : ScriptableObject
    {
        /// <summary>
        /// Resource path used by the default bootstrap. Do not change when relying on built-in loading.
        /// </summary>
        public const string SettingsResourcePath = "CycloneGames.Logger/LoggerSettings";

        public enum ProcessingMode
        {
            AutoDetect = 0,
            ForceThreaded = 1,
            ForceSingleThread = 2
        }

        [Header("Processing")]
        public ProcessingMode processing = ProcessingMode.AutoDetect;

        [Header("Registration")] public bool registerUnityLogger = true;
        [Header("Registration")] public bool registerFileLogger = false;

        [Header("File Logger")]
        public bool usePersistentDataPath = true;
        public string fileName = "App.log";
        public string customFilePath = string.Empty;

        [Header("Defaults")]
        public LogLevel defaultLevel = LogLevel.Info;
        public LogFilter defaultFilter = LogFilter.LogAll;
    }
}


