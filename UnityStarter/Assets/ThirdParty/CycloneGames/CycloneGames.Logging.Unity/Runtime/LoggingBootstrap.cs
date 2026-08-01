using System;
using System.IO;
using System.Threading;
using CycloneGames.Logging;
using UnityEngine;

namespace CycloneGames.Logging.Unity
{
    public static class LoggingBootstrap
    {
        private enum LifecycleState : byte
        {
            Stopped = 0,
            Running = 1,
            ShutdownIncomplete = 2
        }

        private static readonly object LifecycleLock = new object();
        private static LogPipeline _ownedPipeline;
        private static LogPipeline _installedProcessWriter;
        private static int _lifecycleState;
#if UNITY_INCLUDE_TESTS
        internal static Action BeforeProcessWriterInstallTestHook;
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitializeAutomatically()
        {
            try
            {
                LoggingInitializationResult result = Initialize();
                if (result.Status == LoggingInitializationStatus.ShutdownFailed)
                {
                    const string Message = "Automatic bootstrap is blocked because the previously owned pipeline did not finish shutting down.";
                    EmergencyLogWriter.TryWrite(Message);
                }
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
                string failureType = exception.GetType().Name;
                EmergencyLogWriter.TryWrite("Automatic logging initialization failed. " + failureType);
            }
        }

        /// <summary>
        /// Initializes the Unity logging backend once. A null settings value loads the configured
        /// Resources asset and then falls back to package defaults. This method must run on Unity's
        /// main thread.
        /// </summary>
        public static LoggingInitializationResult Initialize(LoggingSettings settings = null)
        {
            LoggingRuntimeHost.EnsureMainThreadAccess();
            lock (LifecycleLock)
            {
                LifecycleState state = (LifecycleState)Volatile.Read(ref _lifecycleState);
                if (state == LifecycleState.ShutdownIncomplete)
                {
                    LogPipeline installed = Volatile.Read(ref _installedProcessWriter);
                    return new LoggingInitializationResult(
                        LoggingInitializationStatus.ShutdownFailed,
                        installed != null && ReferenceEquals(LogRuntime.Writer, installed));
                }

                if (state == LifecycleState.Running)
                {
                    LogPipeline installed = Volatile.Read(ref _installedProcessWriter);
                    return new LoggingInitializationResult(
                        LoggingInitializationStatus.AlreadyInitialized,
                        installed != null && ReferenceEquals(LogRuntime.Writer, installed));
                }

                if (LogRuntime.HasWriter)
                {
                    return new LoggingInitializationResult(
                        LoggingInitializationStatus.ExistingProcessWriterNotOwned,
                        false);
                }

                return InitializeCore(settings == null ? LoadSettings() : settings);
            }
        }

        /// <summary>
        /// Drains and shuts down the owned pipeline before applying the supplied settings.
        /// Initialization does not continue when the previous pipeline cannot stop safely.
        /// </summary>
        public static LoggingReinitializationResult Reinitialize(
            LoggingSettings settings = null,
            LogFlushMode flushMode = LogFlushMode.Buffered)
        {
            LoggingRuntimeHost.EnsureMainThreadAccess();
            lock (LifecycleLock)
            {
                LogPipelineShutdownResult shutdown = ShutdownCore(flushMode);
                if (!shutdown.IsComplete && shutdown.Status != LogPipelineShutdownStatus.NotStarted)
                {
                    return new LoggingReinitializationResult(
                        shutdown,
                        new LoggingInitializationResult(LoggingInitializationStatus.ShutdownFailed, false));
                }

                if (LogRuntime.HasWriter)
                {
                    return new LoggingReinitializationResult(
                        shutdown,
                        new LoggingInitializationResult(
                            LoggingInitializationStatus.ExistingProcessWriterNotOwned,
                            false));
                }

                LoggingInitializationResult initialization = InitializeCore(
                    settings == null ? LoadSettings() : settings);
                return new LoggingReinitializationResult(shutdown, initialization);
            }
        }

        /// <summary>
        /// Removes the owned process writer, drains the owned pipeline, and releases its sinks.
        /// </summary>
        public static LogPipelineShutdownResult Shutdown(LogFlushMode flushMode = LogFlushMode.Buffered)
        {
            LoggingRuntimeHost.EnsureMainThreadAccess();
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
                Volatile.Write(ref _ownedPipeline, null);
                Volatile.Write(ref _lifecycleState, (int)LifecycleState.Stopped);
#if UNITY_INCLUDE_TESTS
                BeforeProcessWriterInstallTestHook = null;
#endif
            }
        }

        private static LoggingInitializationResult InitializeCore(LoggingSettings settings)
        {
            try
            {
                return InitializeCoreTransactional(settings);
            }
            catch
            {
                try
                {
                    LogPipelineShutdownResult rollback = ShutdownCore(LogFlushMode.Buffered);
                    if (!rollback.IsComplete && rollback.Status != LogPipelineShutdownStatus.NotStarted)
                    {
                        EmergencyLogWriter.TryWrite(
                            "Logging initialization rollback did not complete. Ownership was retained for an explicit shutdown retry.");
                    }
                }
                catch (Exception rollbackException) when (!(rollbackException is OutOfMemoryException))
                {
                    EmergencyLogWriter.TryWrite(
                        "Logging initialization rollback failed. Ownership was retained for an explicit shutdown retry. "
                        + rollbackException.GetType().Name);
                }

                throw;
            }
        }

        private static LoggingInitializationResult InitializeCoreTransactional(LoggingSettings settings)
        {
            LogPipelineOptions processingOptions = CreateProcessingOptions(settings);
            bool useUnity = settings == null || settings.registerUnityConsoleLogSink;
            bool useConsole = settings != null && settings.registerConsoleLogSink;
            bool useFile = settings != null && settings.registerFileLogSink;

#if UNITY_SERVER
            useUnity = false;
            useConsole = settings == null || settings.registerConsoleLogSink;
#endif
#if UNITY_WEBGL && !UNITY_EDITOR
            useFile = false;
#endif

            if (!useUnity && !useConsole && !useFile)
            {
                return new LoggingInitializationResult(
                    LoggingInitializationStatus.NoSinksConfigured,
                    false);
            }

            LogPipeline pipeline = CreatePipeline(settings, processingOptions);
            Volatile.Write(ref _ownedPipeline, pipeline);
            bool registeredAny = false;
            if (useUnity)
            {
                UnityConsoleLogSinkOptions unityOptions = CreateUnityConsoleOptions(settings, processingOptions);
                registeredAny |= pipeline.TryAddSink(new UnityConsoleLogSink(unityOptions));
            }

            if (useConsole)
            {
                registeredAny |= pipeline.TryAddSink(new ConsoleLogSink());
            }

            if (useFile && FileLogSink.IsSupported)
            {
                try
                {
                    string filePath = ResolveFilePath(settings);
                    FileLogSinkOptions fileOptions = CreateFileOptions(settings);
                    var fileSink = new FileLogSink(filePath, fileOptions);
                    registeredAny |= pipeline.TryAddSink(fileSink);
                }
                catch (Exception exception) when (!(exception is OutOfMemoryException))
                {
                    string failureType = exception.GetType().Name;
                    EmergencyLogWriter.TryWrite("File sink initialization failed; available Unity or console sinks remain active. " + failureType);
                }
            }

            if (!registeredAny)
            {
                LogPipelineShutdownResult emptyShutdown = pipeline.Shutdown(LogFlushMode.Buffered);
                if (emptyShutdown.IsComplete)
                {
                    Volatile.Write(ref _ownedPipeline, null);
                    LoggingRuntimeHost.ResetAfterOwnedShutdown();
                    return new LoggingInitializationResult(
                        LoggingInitializationStatus.NoSinksConfigured,
                        false);
                }

                Volatile.Write(ref _lifecycleState, (int)LifecycleState.ShutdownIncomplete);
                return new LoggingInitializationResult(
                    LoggingInitializationStatus.ShutdownFailed,
                    false);
            }

            if (settings != null)
            {
                pipeline.SetMinimumSeverity(settings.minimumSeverity);
                pipeline.SetCategoryFilter(settings.categoryFilter);
            }

            LoggingRuntimeHost.EnsureBootstrapInstance();
#if UNITY_INCLUDE_TESTS
            BeforeProcessWriterInstallTestHook?.Invoke();
#endif
            if (LogRuntime.TryInstallWriter(pipeline)
                || ReferenceEquals(LogRuntime.Writer, pipeline))
            {
                Volatile.Write(ref _installedProcessWriter, pipeline);
                Volatile.Write(ref _lifecycleState, (int)LifecycleState.Running);
                return new LoggingInitializationResult(
                    LoggingInitializationStatus.Initialized,
                    true);
            }

            LogPipelineShutdownResult rollback = ShutdownCore(LogFlushMode.Buffered);
            if (!rollback.IsComplete && rollback.Status != LogPipelineShutdownStatus.NotStarted)
            {
                EmergencyLogWriter.TryWrite(
                    "Logging initialization lost the process-writer race and rollback did not complete. Ownership was retained for an explicit shutdown retry.");
                return new LoggingInitializationResult(
                    LoggingInitializationStatus.ShutdownFailed,
                    false);
            }

            return new LoggingInitializationResult(
                LoggingInitializationStatus.ExistingProcessWriterNotOwned,
                false);
        }

        private static LogPipelineShutdownResult ShutdownCore(LogFlushMode flushMode)
        {
            LogPipeline owned = Volatile.Read(ref _ownedPipeline);
            LogPipeline installed = Volatile.Read(ref _installedProcessWriter);
            ResetProcessWriter();
            LogPipelineShutdownResult result;
            if (owned == null)
            {
                result = new LogPipelineShutdownResult(LogPipelineShutdownStatus.NotStarted, 0, true);
            }
            else
            {
                result = owned.Shutdown(flushMode);
            }

            if (result.IsComplete || result.Status == LogPipelineShutdownStatus.NotStarted)
            {
                if (owned != null && result.IsComplete)
                {
                    LoggingRuntimeHost.ResetAfterOwnedShutdown();
                }

                Volatile.Write(ref _ownedPipeline, null);
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

        internal static bool TryGetOwnedPipeline(out LogPipeline pipeline)
        {
            pipeline = Volatile.Read(ref _ownedPipeline);
            return pipeline != null
                && (LifecycleState)Volatile.Read(ref _lifecycleState) == LifecycleState.Running;
        }

        private static void ResetProcessWriter()
        {
            LogPipeline installed = Interlocked.Exchange(ref _installedProcessWriter, null);
            if (installed != null)
            {
                LogRuntime.TryResetWriter(installed);
            }
        }

        private static LoggingSettings LoadSettings()
        {
#if !UNITY_EDITOR
            LoggingSettings buildOverride = Resources.Load<LoggingSettings>(LoggingSettings.BuildOverrideResourcePath);
            if (buildOverride != null)
            {
                return buildOverride;
            }
#endif
            return Resources.Load<LoggingSettings>(LoggingSettings.SettingsResourcePath);
        }

        private static LogPipelineOptions CreateProcessingOptions(LoggingSettings settings)
        {
            if (settings == null)
            {
                return LogPipelineOptions.CreateValidated(null);
            }

            LogQueueOverflowPolicy overflowPolicy = settings.overflowPolicy;
#if UNITY_WEBGL && !UNITY_EDITOR
            if (overflowPolicy == LogQueueOverflowPolicy.Block)
            {
                overflowPolicy = LogQueueOverflowPolicy.DropNewest;
                EmergencyLogWriter.TryWrite(
                    "Block overflow policy is unavailable on WebGL and was replaced with DropNewest.");
            }
#endif

            return LogPipelineOptions.CreateValidated(new LogPipelineOptions
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
                ShutdownDrainTimeoutMs = settings.shutdownDrainTimeoutMs,
                EnqueueBlockTimeoutMs = settings.enqueueBlockTimeoutMs,
                MaintenanceIntervalMs = settings.maintenanceIntervalMs,
                SinkFailureThreshold = settings.sinkFailureThreshold,
                OverflowPolicy = overflowPolicy,
                CriticalSeverity = settings.criticalSeverity
            });
        }

        private static UnityConsoleLogSinkOptions CreateUnityConsoleOptions(
            LoggingSettings settings,
            LogPipelineOptions processingOptions)
        {
            var options = new UnityConsoleLogSinkOptions
            {
                MaximumRetainedEntryCharacters = UnityConsoleLogSinkOptions.EstimateRetainedCharacters(
                    processingOptions.MaxMessageCharacters,
                    processingOptions.MaxCategoryCharacters,
                    processingOptions.MaxSourcePathCharacters),
                ReservedCriticalMessages = processingOptions.ReservedCriticalMessages,
                ReservedCriticalCharacters = processingOptions.ReservedCriticalCharacters,
                CriticalSeverity = processingOptions.CriticalSeverity
            };

            if (settings != null)
            {
                options.MaxQueuedMessages = settings.unityConsoleMaxQueuedMessages;
                options.MaxQueuedCharacters = settings.unityConsoleMaxQueuedCharacters;
                options.OverflowPolicy = settings.unityConsoleOverflowPolicy;
            }

            return UnityConsoleLogSinkOptions.CreateValidated(options);
        }

        private static LogPipeline CreatePipeline(LoggingSettings settings, LogPipelineOptions options)
        {
            LoggingSettings.ExecutionMode mode = settings == null
                ? LoggingSettings.ExecutionMode.Automatic
                : settings.executionMode;

#if UNITY_WEBGL && !UNITY_EDITOR
            return LogPipelineFactory.CreateSingleThreaded(options);
#else
            switch (mode)
            {
                case LoggingSettings.ExecutionMode.SingleThreaded:
                    return LogPipelineFactory.CreateSingleThreaded(options);
                case LoggingSettings.ExecutionMode.Threaded:
                case LoggingSettings.ExecutionMode.Automatic:
                    return LogPipelineFactory.CreateThreaded(options);
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown logging execution mode.");
            }
#endif
        }

        private static FileLogSinkOptions CreateFileOptions(LoggingSettings settings)
        {
            return FileLogSinkOptions.CreateValidated(new FileLogSinkOptions
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

        private static string ResolveFilePath(LoggingSettings settings)
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
                    throw new InvalidOperationException("Logging fileName must remain directly inside Application.persistentDataPath.");
                }

                return combined;
            }

            if (!settings.allowCustomFilePath || string.IsNullOrWhiteSpace(settings.customFilePath))
            {
                throw new InvalidOperationException("A custom logging path requires allowCustomFilePath and a non-empty customFilePath.");
            }

            if (!Path.IsPathFullyQualified(settings.customFilePath))
            {
                throw new InvalidOperationException("Logging customFilePath must be a fully-qualified absolute path.");
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
                throw new InvalidOperationException("Logging fileName must be a portable file name without directory segments.");
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
