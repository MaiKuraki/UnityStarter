using UnityEngine;

namespace CycloneGames.Logger
{
    /// <summary>
    /// Project-level Unity authoring bridge for the pure C# logger runtime.
    /// Runtime state is copied from this asset during bootstrap and is never stored in it.
    /// </summary>
    [CreateAssetMenu(fileName = "LoggerSettings", menuName = "CycloneGames/Logger/Logger Settings", order = 0)]
    public sealed class LoggerSettings : ScriptableObject
    {
        public const string SettingsResourcePath = "CycloneGames.Logger/LoggerSettings";
        public const string BuildOverrideResourcePath = "CycloneGames.Logger/LoggerSettingsBuildOverride";

        public enum ProcessingMode
        {
            AutoDetect = 0,
            ForceThreaded = 1,
            ForceSingleThread = 2
        }

        [Header("Processing")]
        public ProcessingMode processing = ProcessingMode.AutoDetect;
        public int maxQueuedMessages = LoggerProcessingOptions.DefaultMaxQueuedMessages;
        public int maxQueuedCharacters = LoggerProcessingOptions.DefaultMaxQueuedCharacters;
        public int maxMessageCharacters = LoggerProcessingOptions.DefaultMaxMessageCharacters;
        public int maxCategoryCharacters = LoggerProcessingOptions.DefaultMaxCategoryCharacters;
        public int maxSourcePathCharacters = LoggerProcessingOptions.DefaultMaxSourcePathCharacters;
        public int maxMemberNameCharacters = LoggerProcessingOptions.DefaultMaxMemberNameCharacters;
        public int maxFilterCategories = LoggerProcessingOptions.DefaultMaxFilterCategories;
        public int maxFilterCharacters = LoggerProcessingOptions.DefaultMaxFilterCharacters;
        public int reservedCriticalMessages = LoggerProcessingOptions.DefaultReservedCriticalMessages;
        public int reservedCriticalCharacters = LoggerProcessingOptions.DefaultReservedCriticalCharacters;
        public int unityConsoleMaxQueuedMessages = LoggerProcessingOptions.DefaultUnityConsoleMaxQueuedMessages;
        public int unityConsoleMaxQueuedCharacters = LoggerProcessingOptions.DefaultUnityConsoleMaxQueuedCharacters;
        public LogQueueOverflowPolicy unityConsoleOverflowPolicy = LogQueueOverflowPolicy.DropNewest;
        public int shutdownDrainTimeoutMs = LoggerProcessingOptions.DefaultShutdownDrainTimeoutMs;
        public int enqueueBlockTimeoutMs = 1;
        public int maintenanceIntervalMs = LoggerProcessingOptions.DefaultMaintenanceIntervalMs;
        public int sinkFailureThreshold = LoggerProcessingOptions.DefaultSinkFailureThreshold;
        public LogQueueOverflowPolicy overflowPolicy = LogQueueOverflowPolicy.DropNewest;

        [Tooltip("Severity that may use reserved queue capacity. This is not an absolute delivery guarantee.")]
        public LogLevel guaranteedLevel = LogLevel.Error;

        [Header("Registration")]
        public bool registerUnityLogger = true;
        public bool registerConsoleLogger;
        public bool registerFileLogger;

        [Header("File Logger")]
        public bool usePersistentDataPath = true;
        public string fileName = "App.log";
        public bool allowCustomFilePath;
        public string customFilePath = string.Empty;
        public FileMaintenanceMode fileMaintenanceMode = FileMaintenanceMode.Rotate;
        public long maxFileBytes = 10L * 1024L * 1024L;
        public int maxArchiveFiles = 5;
        public int fileFlushBatchSize = 64;
        public int fileFlushIntervalMs = 1000;
        public bool durableFlushOnFatal;
        public LogSourcePathMode fileSourcePathMode = LogSourcePathMode.FileName;

        [Header("Defaults")]
        public LogLevel defaultLevel = LogLevel.Info;
        public LogFilter defaultFilter = LogFilter.LogAll;
    }
}
