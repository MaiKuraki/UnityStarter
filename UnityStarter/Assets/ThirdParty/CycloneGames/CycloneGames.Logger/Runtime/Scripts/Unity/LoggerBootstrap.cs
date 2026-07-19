using System;
using System.IO;
using UnityEngine;

namespace CycloneGames.Logger
{
    public static class LoggerBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            LoggerSettings settings = LoadSettings();
            LoggerProcessingOptions processingOptions = CreateProcessingOptions(settings);
            ConfigureProcessing(settings, processingOptions);
            LoggerUpdater.Configure(processingOptions);

            bool useUnity = settings == null || settings.registerUnityLogger;
            bool useConsole = settings != null && settings.registerConsoleLogger;
            bool useFile = settings != null && settings.registerFileLogger;

#if UNITY_SERVER
            useUnity = false;
            useConsole = settings == null || settings.registerConsoleLogger;
#endif
#if UNITY_WEBGL && !UNITY_EDITOR
            useFile = false;
#endif

            bool registeredAny = false;
            if (useUnity)
            {
                registeredAny |= CLogger.Instance.AddLoggerUnique(new UnityLogger());
            }

            if (useConsole)
            {
                registeredAny |= CLogger.Instance.AddLoggerUnique(new ConsoleLogger());
            }

            if (useFile && FileLogger.IsSupported)
            {
                try
                {
                    string filePath = ResolveFilePath(settings);
                    FileLoggerOptions fileOptions = CreateFileOptions(settings);
                    registeredAny |= CLogger.Instance.AddLoggerUnique(new FileLogger(filePath, fileOptions));
                }
                catch (Exception exception)
                {
                    string failureType = exception.GetType().Name;
                    EmergencyLogger.TryWrite("File sink initialization failed; available Unity or console sinks remain active. " + failureType);
                    Debug.LogError("CycloneGames.Logger: File sink initialization failed; continuing without file output. " + failureType);
                }
            }

            if (registeredAny)
            {
                LoggerUpdater.EnsureInstance();
            }

            if (settings != null && registeredAny)
            {
                CLogger.Instance.SetLogLevel(settings.defaultLevel);
                CLogger.Instance.SetLogFilter(settings.defaultFilter);
            }

            CLogger.ConfigureGlobalStaticLoggingSuppressed(!registeredAny);
        }

        private static LoggerSettings LoadSettings()
        {
#if !UNITY_EDITOR
            LoggerSettings buildOverride = Resources.Load<LoggerSettings>(LoggerSettings.BuildOverrideResourcePath);
            if (buildOverride != null)
            {
                return buildOverride;
            }
#endif
            return Resources.Load<LoggerSettings>(LoggerSettings.SettingsResourcePath);
        }

        private static LoggerProcessingOptions CreateProcessingOptions(LoggerSettings settings)
        {
            if (settings == null)
            {
                return LoggerProcessingOptions.CreateValidated(null);
            }

            LogQueueOverflowPolicy overflowPolicy = settings.overflowPolicy;
#if UNITY_WEBGL && !UNITY_EDITOR
            if (overflowPolicy == LogQueueOverflowPolicy.Block)
            {
                overflowPolicy = LogQueueOverflowPolicy.DropNewest;
                Debug.LogWarning("CycloneGames.Logger: Block overflow policy is not supported on WebGL and was replaced with DropNewest.");
            }
#endif

            return LoggerProcessingOptions.CreateValidated(new LoggerProcessingOptions
            {
                MaxQueuedMessages = settings.maxQueuedMessages,
                MaxQueuedCharacters = settings.maxQueuedCharacters,
                MaxMessageCharacters = settings.maxMessageCharacters,
                MaxCategoryCharacters = settings.maxCategoryCharacters,
                MaxSourcePathCharacters = settings.maxSourcePathCharacters,
                MaxMemberNameCharacters = settings.maxMemberNameCharacters,
                MaxFilterCategories = settings.maxFilterCategories,
                MaxFilterCharacters = settings.maxFilterCharacters,
                ReservedCriticalMessages = settings.reservedCriticalMessages,
                ReservedCriticalCharacters = settings.reservedCriticalCharacters,
                UnityConsoleMaxQueuedMessages = settings.unityConsoleMaxQueuedMessages,
                UnityConsoleMaxQueuedCharacters = settings.unityConsoleMaxQueuedCharacters,
                UnityConsoleOverflowPolicy = settings.unityConsoleOverflowPolicy,
                ShutdownDrainTimeoutMs = settings.shutdownDrainTimeoutMs,
                EnqueueBlockTimeoutMs = settings.enqueueBlockTimeoutMs,
                MaintenanceIntervalMs = settings.maintenanceIntervalMs,
                SinkFailureThreshold = settings.sinkFailureThreshold,
                OverflowPolicy = overflowPolicy,
                CriticalLevel = settings.guaranteedLevel
            });
        }

        private static void ConfigureProcessing(LoggerSettings settings, LoggerProcessingOptions options)
        {
            LoggerSettings.ProcessingMode mode = settings == null
                ? LoggerSettings.ProcessingMode.AutoDetect
                : settings.processing;

#if UNITY_WEBGL && !UNITY_EDITOR
            if (!CLogger.ConfigureSingleThreadedProcessing(options))
            {
                throw new InvalidOperationException("CycloneGames.Logger was accessed before Unity bootstrap could apply the WebGL processing configuration.");
            }
#else
            bool configured;
            switch (mode)
            {
                case LoggerSettings.ProcessingMode.ForceSingleThread:
                    configured = CLogger.ConfigureSingleThreadedProcessing(options);
                    break;
                case LoggerSettings.ProcessingMode.ForceThreaded:
                case LoggerSettings.ProcessingMode.AutoDetect:
                    configured = CLogger.ConfigureThreadedProcessing(options);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown logger processing mode.");
            }

            if (!configured)
            {
                throw new InvalidOperationException("CycloneGames.Logger was accessed before Unity bootstrap could apply LoggerSettings.");
            }
#endif
        }

        private static FileLoggerOptions CreateFileOptions(LoggerSettings settings)
        {
            return FileLoggerOptions.CreateValidated(new FileLoggerOptions
            {
                MaintenanceMode = settings.fileMaintenanceMode,
                MaxFileBytes = settings.maxFileBytes,
                MaxArchiveFiles = settings.maxArchiveFiles,
                FlushBatchSize = settings.fileFlushBatchSize,
                FlushIntervalMs = settings.fileFlushIntervalMs,
                DurableFlushOnFatal = settings.durableFlushOnFatal,
                SourcePathMode = settings.fileSourcePathMode
            });
        }

        private static string ResolveFilePath(LoggerSettings settings)
        {
            if (settings.usePersistentDataPath)
            {
                ValidatePortableFileName(settings.fileName);
                string root = Path.GetFullPath(Application.persistentDataPath);
                string combined = Path.GetFullPath(Path.Combine(root, settings.fileName));
                string parent = Path.GetDirectoryName(combined);
                if (!string.Equals(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                        parent?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                        GetPathComparison()))
                {
                    throw new InvalidOperationException("Logger fileName must remain directly inside Application.persistentDataPath.");
                }

                return combined;
            }

            if (!settings.allowCustomFilePath || string.IsNullOrWhiteSpace(settings.customFilePath))
            {
                throw new InvalidOperationException("A custom logger path requires allowCustomFilePath and a non-empty customFilePath.");
            }

            if (!Path.IsPathFullyQualified(settings.customFilePath))
            {
                throw new InvalidOperationException("Logger customFilePath must be a fully-qualified absolute path.");
            }

            return Path.GetFullPath(settings.customFilePath);
        }

        private static void ValidatePortableFileName(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)
                || Path.IsPathRooted(fileName)
                || fileName == "."
                || fileName == ".."
                || fileName.IndexOf('/') >= 0
                || fileName.IndexOf('\\') >= 0
                || fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new InvalidOperationException("Logger fileName must be a portable file name without directory segments.");
            }
        }

        private static StringComparison GetPathComparison()
        {
            return Application.platform == RuntimePlatform.WindowsPlayer
                || Application.platform == RuntimePlatform.WindowsEditor
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
        }
    }
}
