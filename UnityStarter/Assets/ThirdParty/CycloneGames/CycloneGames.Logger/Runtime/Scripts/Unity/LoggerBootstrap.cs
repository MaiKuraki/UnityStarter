using System;
using System.IO;
using System.Threading;
using CycloneGames.Logging;
using UnityEngine;

namespace CycloneGames.Logger
{
    public static class LoggerBootstrap
    {
        private enum LifecycleState : byte
        {
            Stopped = 0,
            Running = 1,
            ShutdownIncomplete = 2
        }

        private static readonly object LifecycleLock = new object();
        private static CLogger _ownedLogger;
        private static CLogger _installedProcessWriter;
        private static int _lifecycleState;
#if UNITY_INCLUDE_TESTS
        internal static Action BeforeProcessWriterInstallTestHook;
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitializeAutomatically()
        {
            try
            {
                LoggerInitializationResult result = Initialize();
                if (result.Status == LoggerInitializationStatus.ExistingLoggerNotOwned)
                {
                    const string Message = "Automatic bootstrap preserved a CLogger instance owned by another composition root.";
                    EmergencyLogger.TryWrite(Message);
                    Debug.LogError("CycloneGames.Logger: " + Message);
                }
                else if (result.Status == LoggerInitializationStatus.ShutdownFailed)
                {
                    const string Message = "Automatic bootstrap is blocked because the previous owned backend did not finish shutting down.";
                    EmergencyLogger.TryWrite(Message);
                    Debug.LogError("CycloneGames.Logger: " + Message);
                }
            }
            catch (Exception exception)
            {
                string failureType = exception.GetType().Name;
                EmergencyLogger.TryWrite("Automatic logger initialization failed. " + failureType);
                Debug.LogError("CycloneGames.Logger: Automatic initialization failed. " + failureType);
            }
        }

        /// <summary>
        /// Initializes the Unity logging backend once. A null settings value loads the configured
        /// Resources asset and then falls back to package defaults. This method must run on Unity's
        /// main thread.
        /// </summary>
        public static LoggerInitializationResult Initialize(LoggerSettings settings = null)
        {
            LoggerUpdater.EnsureMainThreadAccess();
            lock (LifecycleLock)
            {
                LifecycleState state = (LifecycleState)Volatile.Read(ref _lifecycleState);
                if (state == LifecycleState.ShutdownIncomplete)
                {
                    CLogger installed = Volatile.Read(ref _installedProcessWriter);
                    return new LoggerInitializationResult(
                        LoggerInitializationStatus.ShutdownFailed,
                        installed != null && ReferenceEquals(LogRuntime.Writer, installed));
                }

                if (state == LifecycleState.Running)
                {
                    CLogger installed = Volatile.Read(ref _installedProcessWriter);
                    return new LoggerInitializationResult(
                        LoggerInitializationStatus.AlreadyInitialized,
                        installed != null && ReferenceEquals(LogRuntime.Writer, installed));
                }

                if (CLogger.TryGetInstance(out _))
                {
                    return new LoggerInitializationResult(
                        LoggerInitializationStatus.ExistingLoggerNotOwned,
                        false);
                }

                if (LogRuntime.HasWriter)
                {
                    return new LoggerInitializationResult(
                        LoggerInitializationStatus.ExistingProcessWriterNotOwned,
                        false);
                }

                return InitializeCore(settings == null ? LoadSettings() : settings);
            }
        }

        /// <summary>
        /// Drains and shuts down the owned global backend before applying the supplied settings.
        /// Initialization does not continue when the previous backend cannot stop safely.
        /// </summary>
        public static LoggerReinitializationResult Reinitialize(
            LoggerSettings settings = null,
            LogFlushMode flushMode = LogFlushMode.Buffered)
        {
            LoggerUpdater.EnsureMainThreadAccess();
            lock (LifecycleLock)
            {
                LoggerShutdownResult shutdown = ShutdownCore(flushMode);
                if (!shutdown.IsComplete && shutdown.Status != LoggerShutdownStatus.NotStarted)
                {
                    return new LoggerReinitializationResult(
                        shutdown,
                        new LoggerInitializationResult(LoggerInitializationStatus.ShutdownFailed, false));
                }

                if (CLogger.TryGetInstance(out _))
                {
                    return new LoggerReinitializationResult(
                        shutdown,
                        new LoggerInitializationResult(
                            LoggerInitializationStatus.ExistingLoggerNotOwned,
                            false));
                }

                if (LogRuntime.HasWriter)
                {
                    return new LoggerReinitializationResult(
                        shutdown,
                        new LoggerInitializationResult(
                            LoggerInitializationStatus.ExistingProcessWriterNotOwned,
                            false));
                }

                LoggerInitializationResult initialization = InitializeCore(
                    settings == null ? LoadSettings() : settings);
                return new LoggerReinitializationResult(shutdown, initialization);
            }
        }

        /// <summary>
        /// Removes the owned process writer, drains the global backend, and releases its sinks.
        /// </summary>
        public static LoggerShutdownResult Shutdown(LogFlushMode flushMode = LogFlushMode.Buffered)
        {
            LoggerUpdater.EnsureMainThreadAccess();
            lock (LifecycleLock)
            {
                return ShutdownCore(flushMode);
            }
        }

        internal static void ResetForSubsystemRegistration()
        {
            lock (LifecycleLock)
            {
                ResetProcessWriter();
                Volatile.Write(ref _ownedLogger, null);
                Volatile.Write(ref _lifecycleState, (int)LifecycleState.Stopped);
#if UNITY_INCLUDE_TESTS
                BeforeProcessWriterInstallTestHook = null;
#endif
            }
        }

        private static LoggerInitializationResult InitializeCore(LoggerSettings settings)
        {
            try
            {
                return InitializeCoreTransactional(settings);
            }
            catch
            {
                try
                {
                    LoggerShutdownResult rollback = ShutdownCore(LogFlushMode.Buffered);
                    if (!rollback.IsComplete && rollback.Status != LoggerShutdownStatus.NotStarted)
                    {
                        EmergencyLogger.TryWrite(
                            "Logger initialization rollback did not complete. Ownership was retained for an explicit shutdown retry.");
                    }
                }
                catch (Exception rollbackException)
                {
                    EmergencyLogger.TryWrite(
                        "Logger initialization rollback failed. Ownership was retained for an explicit shutdown retry. "
                        + rollbackException.GetType().Name);
                }

                throw;
            }
        }

        private static LoggerInitializationResult InitializeCoreTransactional(LoggerSettings settings)
        {
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
                registeredAny |= GetOrCreateOwnedLogger().AddLoggerUnique(new UnityLogger());
            }

            if (useConsole)
            {
                registeredAny |= GetOrCreateOwnedLogger().AddLoggerUnique(new ConsoleLogger());
            }

            if (useFile && FileLogger.IsSupported)
            {
                try
                {
                    string filePath = ResolveFilePath(settings);
                    FileLoggerOptions fileOptions = CreateFileOptions(settings);
                    var fileLogger = new FileLogger(filePath, fileOptions);
                    registeredAny |= GetOrCreateOwnedLogger().AddLoggerUnique(fileLogger);
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
                LoggerUpdater.EnsureBootstrapInstance();
            }

            if (settings != null && registeredAny)
            {
                CLogger ownedLogger = Volatile.Read(ref _ownedLogger);
                ownedLogger.SetLogLevel(settings.defaultLevel);
                ownedLogger.SetLogFilter(settings.defaultFilter);
            }

            bool processWriterInstalled = false;
            if (registeredAny)
            {
                CLogger processWriter = Volatile.Read(ref _ownedLogger);
#if UNITY_INCLUDE_TESTS
                BeforeProcessWriterInstallTestHook?.Invoke();
#endif
                if (LogRuntime.TryInstallWriter(processWriter)
                    || ReferenceEquals(LogRuntime.Writer, processWriter))
                {
                    Volatile.Write(ref _installedProcessWriter, processWriter);
                    processWriterInstalled = true;
                }
                else
                {
                    LoggerShutdownResult rollback = ShutdownCore(LogFlushMode.Buffered);
                    if (!rollback.IsComplete && rollback.Status != LoggerShutdownStatus.NotStarted)
                    {
                        EmergencyLogger.TryWrite(
                            "Logger initialization lost the process-writer race and rollback did not complete. Ownership was retained for an explicit shutdown retry.");
                        return new LoggerInitializationResult(
                            LoggerInitializationStatus.ShutdownFailed,
                            false);
                    }

                    return new LoggerInitializationResult(
                        LoggerInitializationStatus.ExistingProcessWriterNotOwned,
                        false);
                }
            }

            Volatile.Write(ref _lifecycleState, (int)LifecycleState.Running);
            return new LoggerInitializationResult(
                registeredAny
                    ? LoggerInitializationStatus.Initialized
                    : LoggerInitializationStatus.NoSinksConfigured,
                processWriterInstalled);
        }

        private static LoggerShutdownResult ShutdownCore(LogFlushMode flushMode)
        {
            CLogger owned = Volatile.Read(ref _ownedLogger);
            CLogger installed = Volatile.Read(ref _installedProcessWriter);
            ResetProcessWriter();
            LoggerShutdownResult result;
            if (owned == null)
            {
                result = new LoggerShutdownResult(LoggerShutdownStatus.NotStarted, 0, true);
            }
            else if (CLogger.TryGetInstance(out CLogger current) && ReferenceEquals(current, owned))
            {
                result = CLogger.Shutdown(flushMode);
            }
            else
            {
                result = owned.ShutdownInstance(flushMode);
            }

            if (result.IsComplete || result.Status == LoggerShutdownStatus.NotStarted)
            {
                if (owned != null && result.IsComplete)
                {
                    LoggerUpdater.ResetAfterOwnedShutdown();
                }

                Volatile.Write(ref _ownedLogger, null);
                Volatile.Write(ref _lifecycleState, (int)LifecycleState.Stopped);
                return result;
            }

            if (installed != null
                && (LogRuntime.TryInstallWriter(installed)
                    || ReferenceEquals(LogRuntime.Writer, installed)))
            {
                Volatile.Write(ref _installedProcessWriter, installed);
            }

            Volatile.Write(ref _lifecycleState, (int)LifecycleState.ShutdownIncomplete);

            return result;
        }

        internal static bool TryGetOwnedLogger(out CLogger logger)
        {
            logger = Volatile.Read(ref _ownedLogger);
            return logger != null
                && (LifecycleState)Volatile.Read(ref _lifecycleState) == LifecycleState.Running;
        }

        private static CLogger GetOrCreateOwnedLogger()
        {
            CLogger owned = Volatile.Read(ref _ownedLogger);
            if (owned != null)
            {
                return owned;
            }

            owned = CLogger.Instance;
            Volatile.Write(ref _ownedLogger, owned);
            return owned;
        }

        private static void ResetProcessWriter()
        {
            CLogger installed = Interlocked.Exchange(ref _installedProcessWriter, null);
            if (installed != null)
            {
                LogRuntime.TryResetWriter(installed);
            }
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
                CriticalLevel = settings.criticalLevel
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
