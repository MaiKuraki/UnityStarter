using CycloneGames.Logging;
using UnityEngine;

namespace CycloneGames.Logging.Unity
{
    /// <summary>
    /// Project-level Unity authoring bridge for the pure C# logging pipeline.
    /// Runtime state is copied from this asset during bootstrap and is never stored in it.
    /// </summary>
    [CreateAssetMenu(fileName = "LoggingSettings", menuName = "CycloneGames/Logging/Settings", order = 0)]
    public sealed class LoggingSettings : ScriptableObject
    {
        public const string SettingsResourcePath = "CycloneGames.Logging.Unity/LoggingSettings";
        public const string BuildOverrideResourcePath = "CycloneGames.Logging.Unity/LoggingSettingsBuildOverride";

        public enum ExecutionMode : byte
        {
            Automatic = 0,
            Threaded = 1,
            SingleThreaded = 2
        }

        [Header("Processing")]
        public ExecutionMode executionMode = ExecutionMode.Automatic;
        public int maxQueuedMessages = LogPipelineOptions.DefaultMaxQueuedMessages;
        public int maxQueuedCharacters = LogPipelineOptions.DefaultMaxQueuedCharacters;
        public int maxMessageCharacters = LogPipelineOptions.DefaultMaxMessageCharacters;
        public int maxCategoryCharacters = LogPipelineOptions.DefaultMaxCategoryCharacters;
        public int maxSourcePathCharacters = LogPipelineOptions.DefaultMaxSourcePathCharacters;
        public int maxMemberNameCharacters = LogPipelineOptions.DefaultMaxMemberNameCharacters;
        public int maxFilterCategories = LogPipelineOptions.DefaultMaxFilterCategories;
        public int maxFilterCharacters = LogPipelineOptions.DefaultMaxFilterCharacters;
        public int reservedCriticalMessages = LogPipelineOptions.DefaultReservedCriticalMessages;
        public int reservedCriticalCharacters = LogPipelineOptions.DefaultReservedCriticalCharacters;
        public int unityConsoleMaxQueuedMessages = UnityConsoleLogSinkOptions.DefaultMaxQueuedMessages;
        public int unityConsoleMaxQueuedCharacters = UnityConsoleLogSinkOptions.DefaultMaxQueuedCharacters;
        public LogQueueOverflowPolicy unityConsoleOverflowPolicy = LogQueueOverflowPolicy.DropNewest;
        public int shutdownDrainTimeoutMs = LogPipelineOptions.DefaultShutdownDrainTimeoutMs;
        public int enqueueBlockTimeoutMs = 1;
        public int maintenanceIntervalMs = LogPipelineOptions.DefaultMaintenanceIntervalMs;
        public int sinkFailureThreshold = LogPipelineOptions.DefaultSinkFailureThreshold;
        public LogQueueOverflowPolicy overflowPolicy = LogQueueOverflowPolicy.DropNewest;

        [Tooltip("Severity that may use reserved queue capacity. This is not an absolute delivery guarantee.")]
        public LogSeverity criticalSeverity = LogSeverity.Error;

        [Header("Registration")]
        public bool registerUnityConsoleLogSink = true;
        public bool registerConsoleLogSink;
        public bool registerFileLogSink;

        [Header("File Sink")]
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

        [Header("Filtering")]
        public LogSeverity minimumSeverity = LogSeverity.Info;
        public LogCategoryFilterMode categoryFilter = LogCategoryFilterMode.All;
    }
}
