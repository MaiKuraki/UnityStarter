using System;
using System.Diagnostics;
using System.Threading;
using CycloneGames.Logging;
using UnityEngine;

namespace CycloneGames.Logging.Unity
{
    public readonly struct UnityConsoleLogSinkStatistics
    {
        public readonly int QueuedCount;
        public readonly int QueuedCharacters;
        public readonly int ReservedCount;
        public readonly int ReservedCharacters;
        public readonly int InFlightCount;
        public readonly int InFlightCharacters;
        public readonly int PeakQueuedCount;
        public readonly int PeakQueuedCharacters;
        public readonly long DroppedMessageCount;
        public readonly long DroppedCriticalCount;
        public readonly long AbandonedOnResetCount;

        internal UnityConsoleLogSinkStatistics(
            int queuedCount,
            int queuedCharacters,
            int reservedCount,
            int reservedCharacters,
            int inFlightCount,
            int inFlightCharacters,
            int peakQueuedCount,
            int peakQueuedCharacters,
            long droppedMessageCount,
            long droppedCriticalCount,
            long abandonedOnResetCount)
        {
            QueuedCount = queuedCount;
            QueuedCharacters = queuedCharacters;
            ReservedCount = reservedCount;
            ReservedCharacters = reservedCharacters;
            InFlightCount = inFlightCount;
            InFlightCharacters = inFlightCharacters;
            PeakQueuedCount = peakQueuedCount;
            PeakQueuedCharacters = peakQueuedCharacters;
            DroppedMessageCount = droppedMessageCount;
            DroppedCriticalCount = droppedCriticalCount;
            AbandonedOnResetCount = abandonedOnResetCount;
        }
    }

    [DefaultExecutionOrder(-1000)]
    internal sealed class LoggingRuntimeHost : MonoBehaviour
    {
        private enum InitializationBlockReason : byte
        {
            None = 0,
            ShutdownIncomplete = 1,
            ExternalAdapterOwner = 2
        }

        internal enum SubsystemResetStatus : byte
        {
            Reset = 0,
            OwnedShutdownIncomplete = 1,
            ExternalAdaptersPreserved = 2
        }

        internal readonly struct Reservation
        {
            internal readonly int Characters;
            internal readonly int Generation;

            internal Reservation(int characters, int generation)
            {
                Characters = characters;
                Generation = generation;
            }
        }

        private struct LogEntry
        {
            internal LogSeverity Severity;
            internal string Message;
#if UNITY_EDITOR
            internal string SourcePath;
            internal int LineNumber;
            internal int RetainedCharacters;
#endif
        }

#if UNITY_EDITOR
        internal delegate bool EditorConsoleWriter(LogSeverity level, string message, string sourcePath, int lineNumber);
#endif

        private const int DefaultPumpItems = 256;
        private const int CorePumpBudgetMilliseconds = 1;
        private const int UnityConsoleBudgetMilliseconds = 2;

        private static readonly object QueueLock = new object();

        private static LoggingRuntimeHost _instance;
        private static LogEntry[] _entries = Array.Empty<LogEntry>();
        private static int _head;
        private static int _count;
        private static int _reservedCount;
        private static int _reservedCharacters;
        private static int _queuedCharacters;
        private static int _inFlightCount;
        private static int _inFlightCharacters;
        private static int _peakCount;
        private static int _peakCharacters;
        private static int _mainThreadId;
        private static int _generation;
        private static int _adapterCount;
        private static bool _bootstrapHostOwned;
        private static bool _hostCleanupPending;
        private static LogPipeline _pumpDisabledPipeline;
        private static int _maxQueuedCharacters;
        private static int _reservedCriticalMessages;
        private static int _reservedCriticalCharacters;
        private static LogQueueOverflowPolicy _overflowPolicy = LogQueueOverflowPolicy.DropNewest;
        private static LogSeverity _criticalSeverity = LogSeverity.Error;
        private static long _droppedCount;
        private static long _droppedCriticalCount;
        private static long _abandonedOnResetCount;
        private static bool _quitStarted;
        private static bool _quitting;
        private static volatile bool _initializationBlocked;
        private static InitializationBlockReason _initializationBlockReason;
#if UNITY_EDITOR
        private static EditorConsoleWriter _editorConsoleWriter;
#endif
#if UNITY_INCLUDE_TESTS
        internal static Action BeforeHostPublishTestHook;
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            ResetStaticStateCore(true);
        }

        private static SubsystemResetStatus ResetStaticStateCore(bool reportDiagnostics)
        {
            CaptureMainThreadForLifecycle();
#if UNITY_INCLUDE_TESTS
            BeforeHostPublishTestHook = null;
#endif

            LogPipelineShutdownResult ownedResult = LoggingBootstrap.Shutdown(LogFlushMode.Buffered);
            if (!ownedResult.IsComplete && ownedResult.Status != LogPipelineShutdownStatus.NotStarted)
            {
                lock (QueueLock)
                {
                    _initializationBlocked = true;
                    _initializationBlockReason = InitializationBlockReason.ShutdownIncomplete;
                }

                if (reportDiagnostics)
                {
                    EmergencyLogWriter.TryWrite(
                        "The bootstrap-owned logging pipeline did not stop during subsystem reset. New initialization is blocked to preserve ownership safety.");
                }

                return SubsystemResetStatus.OwnedShutdownIncomplete;
            }

            bool explicitAdaptersSurvived;
            lock (QueueLock)
            {
                explicitAdaptersSurvived = _adapterCount != 0;
                if (explicitAdaptersSurvived)
                {
                    _initializationBlocked = true;
                    _initializationBlockReason = InitializationBlockReason.ExternalAdapterOwner;
                }
            }

            if (explicitAdaptersSurvived)
            {
                if (reportDiagnostics)
                {
                    EmergencyLogWriter.TryWrite(
                        "Explicit UnityConsoleLogSink owners survived subsystem reset. Their host and queue were preserved; dispose those owners before starting package bootstrap.");
                }

                return SubsystemResetStatus.ExternalAdaptersPreserved;
            }

            LoggingBootstrap.ResetForSubsystemRegistration();
            LogMemoryPools.ClearIdleEntries();
            DrainUnityQueue(int.MaxValue, 50);

            int abandonedEntries = 0;
            LoggingRuntimeHost previousInstance = null;
            lock (QueueLock)
            {
                abandonedEntries = _count + _inFlightCount;
                _abandonedOnResetCount += abandonedEntries;
                previousInstance = _instance;
                _instance = null;
                _entries = Array.Empty<LogEntry>();
                _head = 0;
                _count = 0;
                _reservedCount = 0;
                _reservedCharacters = 0;
                _queuedCharacters = 0;
                _inFlightCount = 0;
                _inFlightCharacters = 0;
                _peakCount = 0;
                _peakCharacters = 0;
                _maxQueuedCharacters = 0;
                _reservedCriticalMessages = 0;
                _reservedCriticalCharacters = 0;
                _overflowPolicy = LogQueueOverflowPolicy.DropNewest;
                _criticalSeverity = LogSeverity.Error;
                _droppedCount = 0;
                _droppedCriticalCount = 0;
                _generation = unchecked(_generation + 1);
                _quitStarted = false;
                _quitting = false;
                _initializationBlocked = false;
                _initializationBlockReason = InitializationBlockReason.None;
                _bootstrapHostOwned = false;
                _hostCleanupPending = false;
                _pumpDisabledPipeline = null;
            }

            DestroyHost(previousInstance);

            if (abandonedEntries > 0)
            {
                if (reportDiagnostics)
                {
                    EmergencyLogWriter.TryWrite(
                        "Unity handoff entries could not be drained during subsystem reset and were explicitly abandoned: "
                        + abandonedEntries + ".");
                }
            }

#if UNITY_EDITOR
            LoggingEditorLinkRegistry.Reset();
            LoggingEditorPathResolver.Reset();
#endif
            return SubsystemResetStatus.Reset;
        }

        internal static void Configure(UnityConsoleLogSinkOptions options)
        {
            EnsureMainThreadAccess();
            UnityConsoleLogSinkOptions validated = UnityConsoleLogSinkOptions.CreateValidated(options);
#if UNITY_EDITOR
            LoggingEditorPathResolver.Configure(
                Application.dataPath,
                Application.platform == RuntimePlatform.WindowsEditor);
#endif
            lock (QueueLock)
            {
                TryReleaseExternalOwnershipBlockNoLock();
                if (_initializationBlocked)
                {
                    throw new InvalidOperationException("Logging runtime initialization is blocked because the previous owner did not stop safely.");
                }

                if (_quitting)
                {
                    throw new InvalidOperationException("Unity logging queue cannot be configured after shutdown has started.");
                }

                if (_count != 0 || _reservedCount != 0 || _inFlightCount != 0)
                {
                    throw new InvalidOperationException("Unity logging queue cannot be reconfigured while it contains messages.");
                }

                if (_adapterCount != 0)
                {
                    throw new InvalidOperationException("Unity logging queue cannot be reconfigured while adapters are registered.");
                }

                ApplyConfigurationNoLock(validated);
            }
        }

        internal static void EnsureInstance()
        {
            if (_instance != null)
            {
                return;
            }

            if (_initializationBlocked)
            {
                throw new InvalidOperationException("Logging runtime initialization is blocked because the previous owner did not stop safely.");
            }

            EnsureMainThreadAccess();

            GameObject gameObject = null;
            try
            {
                gameObject = new GameObject("CycloneGames.Logging.RuntimeHost");
                gameObject.hideFlags = HideFlags.HideAndDontSave;
                if (Application.isPlaying)
                {
                    DontDestroyOnLoad(gameObject);
                }

                LoggingRuntimeHost instance = gameObject.AddComponent<LoggingRuntimeHost>();
#if UNITY_INCLUDE_TESTS
                BeforeHostPublishTestHook?.Invoke();
#endif
                Application.quitting -= OnApplicationQuitting;
                Application.quitting += OnApplicationQuitting;
                _instance = instance;
            }
            catch
            {
                try
                {
                    Application.quitting -= OnApplicationQuitting;
                }
                finally
                {
                    DestroyHostGameObject(gameObject);
                }

                throw;
            }
        }

        internal static void EnsureBootstrapInstance()
        {
            EnsureMainThreadAccess();
            lock (QueueLock)
            {
                TryReleaseExternalOwnershipBlockNoLock();
                if (_initializationBlocked)
                {
                    throw new InvalidOperationException("Logging runtime initialization is blocked because the previous owner did not stop safely.");
                }

                _bootstrapHostOwned = true;
                _hostCleanupPending = false;
            }

            try
            {
                EnsureInstance();
            }
            catch
            {
                lock (QueueLock)
                {
                    _bootstrapHostOwned = false;
                }

                throw;
            }
        }

        internal static bool TryReserve(LogSeverity level, int estimatedCharacters, out Reservation reservation)
        {
            lock (QueueLock)
            {
                if (_quitting)
                {
                    reservation = default;
                    RecordDropNoLock(level);
                    return false;
                }

                if (estimatedCharacters > _maxQueuedCharacters)
                {
                    reservation = default;
                    RecordDropNoLock(level);
                    return false;
                }

                int reservedCharacters = Math.Max(estimatedCharacters, 0);
                while (!HasCapacityNoLock(level, reservedCharacters))
                {
                    if (!TryEvictNoLock(level))
                    {
                        reservation = default;
                        RecordDropNoLock(level);
                        return false;
                    }
                }

                _reservedCount++;
                _reservedCharacters += reservedCharacters;
                reservation = new Reservation(reservedCharacters, _generation);
                return true;
            }
        }

        internal static int RegisterAdapter()
        {
            EnsureMainThreadAccess();
            lock (QueueLock)
            {
                TryReleaseExternalOwnershipBlockNoLock();
                if (_initializationBlocked || _quitStarted || _quitting)
                {
                    throw new InvalidOperationException("Unity logging adapter registration is unavailable while runtime initialization is blocked or shutting down.");
                }

                if (_entries.Length == 0)
                {
                    ApplyConfigurationNoLock(UnityConsoleLogSinkOptions.CreateValidated(null));
                }

                _adapterCount++;
                return _generation;
            }
        }

        internal static void UnregisterAdapter(int generation)
        {
            bool cleanupOnCurrentThread = false;
            lock (QueueLock)
            {
                if (generation == _generation && _adapterCount > 0)
                {
                    _adapterCount--;
                    if (_adapterCount == 0)
                    {
                        TryReleaseExternalOwnershipBlockNoLock();
                        if (!_bootstrapHostOwned)
                        {
                            _hostCleanupPending = true;
                            cleanupOnCurrentThread = Environment.CurrentManagedThreadId == _mainThreadId;
                        }
                    }
                }
            }

            if (cleanupOnCurrentThread)
            {
                CleanupUnusedHost();
            }
        }

        internal static bool Commit(
            LogSeverity level,
            string message,
            Reservation reservation
#if UNITY_EDITOR
            ,
            string sourcePath = null,
            int lineNumber = 0
#endif
        )
        {
            lock (QueueLock)
            {
                if (reservation.Generation != _generation)
                {
                    RecordDropNoLock(level);
                    return false;
                }

                ReleaseReservationNoLock(reservation.Characters);
                if (_quitting)
                {
                    RecordDropNoLock(level);
                    return false;
                }

                long actualCharacters = message?.Length ?? 0;
#if UNITY_EDITOR
                actualCharacters += sourcePath?.Length ?? 0;
#endif
                if (actualCharacters > _maxQueuedCharacters)
                {
                    RecordDropNoLock(level);
                    return false;
                }

                int characters = (int)actualCharacters;

                if (characters > reservation.Characters)
                {
                    RecordDropNoLock(level);
                    return false;
                }

                while (!HasCapacityNoLock(level, characters))
                {
                    if (!TryEvictNoLock(level))
                    {
                        RecordDropNoLock(level);
                        return false;
                    }
                }

                int tail = (_head + _count) % _entries.Length;
                _entries[tail].Severity = level;
                _entries[tail].Message = message;
#if UNITY_EDITOR
                _entries[tail].SourcePath = sourcePath;
                _entries[tail].LineNumber = lineNumber;
                _entries[tail].RetainedCharacters = characters;
#endif
                _count++;
                _queuedCharacters += characters;
                int retainedCount = _count + _inFlightCount;
                if (retainedCount > _peakCount)
                {
                    _peakCount = retainedCount;
                }

                int retainedCharacters = _queuedCharacters + _inFlightCharacters;
                if (retainedCharacters > _peakCharacters)
                {
                    _peakCharacters = retainedCharacters;
                }

                return true;
            }
        }

        internal static void CancelReservation(Reservation reservation)
        {
            lock (QueueLock)
            {
                if (reservation.Generation == _generation)
                {
                    ReleaseReservationNoLock(reservation.Characters);
                }
            }
        }

#if UNITY_EDITOR
        internal static void SetEditorConsoleWriter(EditorConsoleWriter writer)
        {
            Volatile.Write(ref _editorConsoleWriter, writer);
        }
#endif

        internal static UnityConsoleLogSinkStatistics GetStatistics()
        {
            lock (QueueLock)
            {
                return new UnityConsoleLogSinkStatistics(
                    _count,
                    _queuedCharacters,
                    _reservedCount,
                    _reservedCharacters,
                    _inFlightCount,
                    _inFlightCharacters,
                    _peakCount,
                    _peakCharacters,
                    _droppedCount,
                    _droppedCriticalCount,
                    _abandonedOnResetCount);
            }
        }

        internal static bool TryFlushUnityQueue(int budgetMilliseconds)
        {
            if (Environment.CurrentManagedThreadId != _mainThreadId)
            {
                return IsQueueIdle();
            }

            DrainUnityQueue(int.MaxValue, Math.Max(budgetMilliseconds, 0));
            return IsQueueIdle();
        }

        internal static void Shutdown(bool drain)
        {
            Application.quitting -= OnApplicationQuitting;
            lock (QueueLock)
            {
                _quitStarted = true;
                _quitting = true;
                _bootstrapHostOwned = false;
                _hostCleanupPending = false;
            }

            if (drain && Environment.CurrentManagedThreadId == _mainThreadId)
            {
                DrainUnityQueue(int.MaxValue, 50);
            }
            else
            {
                DropAllQueued();
            }

            if (_instance == null)
            {
                return;
            }

            LoggingRuntimeHost instance = _instance;
            _instance = null;
            if (Environment.CurrentManagedThreadId != _mainThreadId)
            {
                return;
            }

            DestroyHost(instance);
        }

        /// <summary>
        /// Releases the hidden Unity host after the LoggingBootstrap-owned LogPipeline has stopped.
        /// A clean reset keeps the queue reusable for an immediate Reinitialize or for an
        /// EditMode/PlayMode ownership handoff when domain reload is disabled.
        /// </summary>
        internal static void ResetAfterOwnedShutdown()
        {
            EnsureMainThreadAccess();

            DrainUnityQueue(int.MaxValue, 50);

            LoggingRuntimeHost instance;
            lock (QueueLock)
            {
                _bootstrapHostOwned = false;
                if (_adapterCount != 0)
                {
                    // Explicit UnityConsoleLogSink adapters are not owned by LoggingBootstrap. Keep their
                    // host and queue intact instead of invalidating their generation.
                    return;
                }

                instance = _instance;
                _instance = null;
                _entries = Array.Empty<LogEntry>();
                _head = 0;
                _count = 0;
                _reservedCount = 0;
                _reservedCharacters = 0;
                _queuedCharacters = 0;
                _inFlightCount = 0;
                _inFlightCharacters = 0;
                _peakCount = 0;
                _peakCharacters = 0;
                _maxQueuedCharacters = 0;
                _reservedCriticalMessages = 0;
                _reservedCriticalCharacters = 0;
                _overflowPolicy = LogQueueOverflowPolicy.DropNewest;
                _criticalSeverity = LogSeverity.Error;
                _droppedCount = 0;
                _droppedCriticalCount = 0;
                _abandonedOnResetCount = 0;
                _generation = unchecked(_generation + 1);
                _quitStarted = false;
                _quitting = false;
                _initializationBlocked = false;
                _initializationBlockReason = InitializationBlockReason.None;
                _hostCleanupPending = false;
                _pumpDisabledPipeline = null;
                _mainThreadId = Environment.CurrentManagedThreadId;
            }

            Application.quitting -= OnApplicationQuitting;
            DestroyHost(instance);

#if UNITY_EDITOR
            LoggingEditorLinkRegistry.Reset();
            LoggingEditorPathResolver.Reset();
#endif
        }

        internal static void CaptureMainThreadForLifecycle()
        {
            Volatile.Write(ref _mainThreadId, Environment.CurrentManagedThreadId);
        }

        internal static void EnsureMainThreadAccess()
        {
            int mainThreadId = Volatile.Read(ref _mainThreadId);
            if (mainThreadId == 0 || Environment.CurrentManagedThreadId != mainThreadId)
            {
                throw new InvalidOperationException(
                    "LoggingBootstrap must be called from the Unity main thread after its lifecycle composition root has initialized.");
            }
        }

        internal static SubsystemResetStatus ResetForTests()
        {
            return ResetStaticStateCore(false);
        }

#if UNITY_INCLUDE_TESTS
        internal static void ProcessApplicationQuittingForTests()
        {
            OnApplicationQuitting();
        }

        internal static int ConfiguredMessageCapacityForTests
        {
            get
            {
                lock (QueueLock)
                {
                    return _entries.Length;
                }
            }
        }
#endif

        private void Update()
        {
            PumpOnce();
        }

        internal static void PumpOnce()
        {
            if (Environment.CurrentManagedThreadId != Volatile.Read(ref _mainThreadId))
            {
                return;
            }

            if (LoggingBootstrap.TryGetOwnedPipeline(out LogPipeline pipeline))
            {
                PumpPipelineWithinBudget(pipeline);
            }

            DrainUnityQueue(DefaultPumpItems, UnityConsoleBudgetMilliseconds);
            CleanupUnusedHost();
        }

        internal static void PumpPipelineWithinBudget(LogPipeline pipeline)
        {
            if (pipeline == null
                || ReferenceEquals(Volatile.Read(ref _pumpDisabledPipeline), pipeline))
            {
                return;
            }

            try
            {
                pipeline.PumpWithinBudget(DefaultPumpItems, CorePumpBudgetMilliseconds);
            }
            catch
            {
                Volatile.Write(ref _pumpDisabledPipeline, pipeline);
                throw;
            }
        }

        private void OnApplicationPause(bool paused)
        {
            if (!paused || !LoggingBootstrap.TryGetOwnedPipeline(out LogPipeline pipeline))
            {
                return;
            }

            pipeline.TryFlush(LogFlushMode.Buffered, 50);
            DrainUnityQueue(int.MaxValue, 20);
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private static void OnApplicationQuitting()
        {
#if UNITY_EDITOR
            LogPipelineShutdownResult editorResult = LoggingBootstrap.Shutdown(LogFlushMode.Buffered);
            if (!editorResult.IsComplete && editorResult.Status != LogPipelineShutdownStatus.NotStarted)
            {
                EmergencyLogWriter.TryWrite(
                    "Editor Play Mode exit could not stop the logging pipeline. Ownership is retained so shutdown can be retried.");
            }

            return;
#else
            lock (QueueLock)
            {
                if (_quitStarted)
                {
                    return;
                }

                _quitStarted = true;
            }

            LogPipelineShutdownResult ownedResult = LoggingBootstrap.Shutdown(LogFlushMode.Buffered);
            if (!ownedResult.IsComplete && ownedResult.Status != LogPipelineShutdownStatus.NotStarted)
            {
                EmergencyLogWriter.TryWrite(
                    "Application quit could not stop the bootstrap-owned logging pipeline before terminal shutdown.");
            }

            lock (QueueLock)
            {
                _quitting = true;
            }

            DrainUnityQueue(int.MaxValue, 50);
#endif
        }

        private static void DestroyHost(LoggingRuntimeHost instance)
        {
            if (instance == null)
            {
                return;
            }

            DestroyHostGameObject(instance.gameObject);
        }

        private static void DestroyHostGameObject(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return;
            }

#if UNITY_EDITOR
            DestroyImmediate(gameObject);
#else
            Destroy(gameObject);
#endif
        }

        private static void CleanupUnusedHost()
        {
            if (Environment.CurrentManagedThreadId != Volatile.Read(ref _mainThreadId))
            {
                return;
            }

            LoggingRuntimeHost instance;
            lock (QueueLock)
            {
                if (!_hostCleanupPending || _bootstrapHostOwned || _adapterCount != 0)
                {
                    return;
                }

                if (_count != 0 || _reservedCount != 0 || _inFlightCount != 0)
                {
                    return;
                }

                instance = _instance;
                _instance = null;
                _hostCleanupPending = false;
                ReleaseConfigurationNoLock();
            }

            Application.quitting -= OnApplicationQuitting;
            DestroyHost(instance);
        }

        private static void TryReleaseExternalOwnershipBlockNoLock()
        {
            if (_initializationBlockReason != InitializationBlockReason.ExternalAdapterOwner
                || _adapterCount != 0)
            {
                return;
            }

            _initializationBlocked = false;
            _initializationBlockReason = InitializationBlockReason.None;
        }

        private static void ApplyConfigurationNoLock(UnityConsoleLogSinkOptions options)
        {
            _entries = new LogEntry[options.MaxQueuedMessages];
            _head = 0;
            _maxQueuedCharacters = options.MaxQueuedCharacters;
            _reservedCriticalMessages = options.ReservedCriticalMessages;
            _reservedCriticalCharacters = options.ReservedCriticalCharacters;
            _overflowPolicy = options.OverflowPolicy;
            _criticalSeverity = options.CriticalSeverity;
        }

        private static void ReleaseConfigurationNoLock()
        {
            _entries = Array.Empty<LogEntry>();
            _head = 0;
            _maxQueuedCharacters = 0;
            _reservedCriticalMessages = 0;
            _reservedCriticalCharacters = 0;
            _overflowPolicy = LogQueueOverflowPolicy.DropNewest;
            _criticalSeverity = LogSeverity.Error;
            _peakCount = 0;
            _peakCharacters = 0;
        }

        private static void DrainUnityQueue(int maxItems, int budgetMilliseconds)
        {
            long start = Stopwatch.GetTimestamp();
            long budgetTicks = budgetMilliseconds <= 0
                ? long.MaxValue
                : Stopwatch.Frequency * budgetMilliseconds / 1000L;
            int processed = 0;
            while (processed < maxItems && TryDequeue(out LogEntry entry))
            {
                try
                {
#if UNITY_EDITOR
                    LogToUnity(entry.Severity, entry.Message, entry.SourcePath, entry.LineNumber);
#else
                    LogToUnity(entry.Severity, entry.Message);
#endif
                }
                finally
                {
                    CompleteProcessing(GetRetainedCharacters(entry));
                }

                processed++;
                if (Stopwatch.GetTimestamp() - start >= budgetTicks)
                {
                    break;
                }
            }
        }

        [HideInCallstack]
        private static void LogToUnity(
            LogSeverity level,
            string message
#if UNITY_EDITOR
            , string sourcePath,
            int lineNumber
#endif
        )
        {
#if UNITY_EDITOR
            EditorConsoleWriter editorWriter = Volatile.Read(ref _editorConsoleWriter);
            if (editorWriter != null)
            {
                try
                {
                    if (editorWriter(level, message, sourcePath, lineNumber))
                    {
                        return;
                    }
                }
                catch (Exception exception) when (!(exception is OutOfMemoryException))
                {
                    // The public Unity logging path remains the non-lossy fallback when Editor internals change.
                }
            }
#endif

            LogType logType;
            switch (level)
            {
                case LogSeverity.Warning:
                    logType = LogType.Warning;
                    break;
                case LogSeverity.Error:
                case LogSeverity.Fatal:
                    logType = LogType.Error;
                    break;
                default:
                    logType = LogType.Log;
                    break;
            }

            UnityConsoleOutput.Write(logType, message);
        }

        private static bool TryDequeue(out LogEntry entry)
        {
            lock (QueueLock)
            {
                if (_count == 0)
                {
                    entry = default;
                    return false;
                }

                entry = _entries[_head];
                _entries[_head] = default;
                _head = (_head + 1) % _entries.Length;
                _count--;
                int characters = GetRetainedCharacters(entry);
                _queuedCharacters -= characters;
                _inFlightCount++;
                _inFlightCharacters += characters;
                return true;
            }
        }

        private static void CompleteProcessing(int characters)
        {
            lock (QueueLock)
            {
                if (_inFlightCount > 0)
                {
                    _inFlightCount--;
                }

                _inFlightCharacters -= Math.Max(characters, 0);
                if (_inFlightCharacters < 0)
                {
                    _inFlightCharacters = 0;
                }
            }
        }

        private static bool IsQueueIdle()
        {
            lock (QueueLock)
            {
                return _count == 0
                    && _reservedCount == 0
                    && _inFlightCount == 0;
            }
        }

        private static bool HasCapacityNoLock(LogSeverity level, int characters)
        {
            bool critical = level >= _criticalSeverity;
            int messageLimit = critical ? _entries.Length : _entries.Length - _reservedCriticalMessages;
            int characterLimit = critical ? _maxQueuedCharacters : _maxQueuedCharacters - _reservedCriticalCharacters;
            return _count + _inFlightCount + _reservedCount < messageLimit
                && (long)_queuedCharacters + _inFlightCharacters + _reservedCharacters + characters <= characterLimit;
        }

        private static bool TryEvictNoLock(LogSeverity incomingLevel)
        {
            bool incomingCritical = incomingLevel >= _criticalSeverity;
            int offset = FindOldestNormalOffsetNoLock();
            if (offset < 0)
            {
                if (!incomingCritical || _overflowPolicy != LogQueueOverflowPolicy.DropOldest || _count == 0)
                {
                    return false;
                }

                offset = 0;
            }
            else if (!incomingCritical && _overflowPolicy != LogQueueOverflowPolicy.DropOldest)
            {
                return false;
            }

            LogEntry dropped = RemoveAtOffsetNoLock(offset);
            RecordDropNoLock(dropped.Severity);
            return true;
        }

        private static int FindOldestNormalOffsetNoLock()
        {
            for (int offset = 0; offset < _count; offset++)
            {
                int index = (_head + offset) % _entries.Length;
                if (_entries[index].Severity < _criticalSeverity)
                {
                    return offset;
                }
            }

            return -1;
        }

        private static LogEntry RemoveAtOffsetNoLock(int offset)
        {
            int index = (_head + offset) % _entries.Length;
            LogEntry removed = _entries[index];
            if (offset == 0)
            {
                _entries[_head] = default;
                _head = (_head + 1) % _entries.Length;
                _count--;
                _queuedCharacters -= GetRetainedCharacters(removed);
                return removed;
            }

            for (int current = offset; current < _count - 1; current++)
            {
                int destination = (_head + current) % _entries.Length;
                int source = (_head + current + 1) % _entries.Length;
                _entries[destination] = _entries[source];
            }

            int tail = (_head + _count - 1) % _entries.Length;
            _entries[tail] = default;
            _count--;
            _queuedCharacters -= GetRetainedCharacters(removed);
            return removed;
        }

        private static int GetRetainedCharacters(LogEntry entry)
        {
#if UNITY_EDITOR
            return entry.RetainedCharacters;
#else
            return entry.Message?.Length ?? 0;
#endif
        }

        private static void ReleaseReservationNoLock(int reservedCharacters)
        {
            if (_reservedCount <= 0)
            {
                return;
            }

            _reservedCount--;
            _reservedCharacters -= Math.Min(Math.Max(reservedCharacters, 0), _maxQueuedCharacters);
            if (_reservedCharacters < 0)
            {
                _reservedCharacters = 0;
            }
        }

        private static void RecordDropNoLock(LogSeverity level)
        {
            _droppedCount++;
            if (level >= _criticalSeverity)
            {
                _droppedCriticalCount++;
            }
        }

        private static void DropAllQueued()
        {
            lock (QueueLock)
            {
                while (_count > 0)
                {
                    LogEntry dropped = RemoveAtOffsetNoLock(0);
                    RecordDropNoLock(dropped.Severity);
                }
            }
        }
    }
}
