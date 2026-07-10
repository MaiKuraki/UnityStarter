using System;
using System.Diagnostics;
using System.Threading;
using UnityEngine;

namespace CycloneGames.Logger
{
    public readonly struct UnityLoggerStatistics
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

        internal UnityLoggerStatistics(
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
    internal sealed class LoggerUpdater : MonoBehaviour
    {
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
            internal LogLevel Level;
            internal string Message;
        }

        private const int DefaultPumpItems = 256;
        private const int CorePumpBudgetMilliseconds = 1;
        private const int UnityConsoleBudgetMilliseconds = 2;

        private static readonly object QueueLock = new object();
        private static readonly object[] LogFormatArguments = new object[1];

        private static LoggerUpdater _instance;
        private static LogEntry[] _entries = new LogEntry[LoggerProcessingOptions.DefaultUnityConsoleMaxQueuedMessages];
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
        private static int _maxQueuedCharacters = LoggerProcessingOptions.DefaultUnityConsoleMaxQueuedCharacters;
        private static int _reservedCriticalMessages = LoggerProcessingOptions.DefaultReservedCriticalMessages;
        private static int _reservedCriticalCharacters = LoggerProcessingOptions.DefaultReservedCriticalCharacters;
        private static LogQueueOverflowPolicy _overflowPolicy = LogQueueOverflowPolicy.DropNewest;
        private static LogLevel _criticalLevel = LogLevel.Error;
        private static long _droppedCount;
        private static long _droppedCriticalCount;
        private static long _abandonedOnResetCount;
        private static bool _quitStarted;
        private static bool _quitting;
        private static volatile bool _initializationBlocked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            LoggerShutdownResult resetResult = CLogger.ResetForUnitySubsystemRegistration();
            if (!resetResult.IsComplete && resetResult.Status != LoggerShutdownStatus.NotStarted)
            {
                lock (QueueLock)
                {
                    _mainThreadId = Environment.CurrentManagedThreadId;
                    _quitStarted = true;
                    _quitting = true;
                    _initializationBlocked = true;
                }

                CLogger.CompleteUnitySubsystemRegistrationReset(false);
                UnityEngine.Debug.LogError("CycloneGames.Logger: The previous runtime did not stop during subsystem reset. New logger initialization is blocked to preserve ownership safety.");
                return;
            }

            DrainUnityQueue(int.MaxValue, 50);

            bool explicitAdaptersSurvived;
            int abandonedEntries = 0;
            lock (QueueLock)
            {
                explicitAdaptersSurvived = _adapterCount != 0;
                if (explicitAdaptersSurvived)
                {
                    _mainThreadId = Environment.CurrentManagedThreadId;
                    _quitStarted = true;
                    _quitting = true;
                    _initializationBlocked = true;
                }
                else
                {
                    abandonedEntries = _count + _inFlightCount;
                    _abandonedOnResetCount += abandonedEntries;
                    _instance = null;
                    _mainThreadId = Environment.CurrentManagedThreadId;
                    _entries = new LogEntry[LoggerProcessingOptions.DefaultUnityConsoleMaxQueuedMessages];
                    _head = 0;
                    _count = 0;
                    _reservedCount = 0;
                    _reservedCharacters = 0;
                    _queuedCharacters = 0;
                    _inFlightCount = 0;
                    _inFlightCharacters = 0;
                    _peakCount = 0;
                    _peakCharacters = 0;
                    _maxQueuedCharacters = LoggerProcessingOptions.DefaultUnityConsoleMaxQueuedCharacters;
                    _reservedCriticalMessages = LoggerProcessingOptions.DefaultReservedCriticalMessages;
                    _reservedCriticalCharacters = LoggerProcessingOptions.DefaultReservedCriticalCharacters;
                    _overflowPolicy = LogQueueOverflowPolicy.DropNewest;
                    _criticalLevel = LogLevel.Error;
                    _droppedCount = 0;
                    _droppedCriticalCount = 0;
                    _generation = unchecked(_generation + 1);
                    _quitStarted = false;
                    _quitting = false;
                    _initializationBlocked = false;
                }
            }

            CLogger.CompleteUnitySubsystemRegistrationReset(!explicitAdaptersSurvived);
            if (explicitAdaptersSurvived)
            {
                UnityEngine.Debug.LogError(
                    "CycloneGames.Logger: Explicit UnityLogger owners survived subsystem reset. Dispose their CLogger/UnityLogger owners before starting a new runtime.");
                return;
            }

            if (abandonedEntries > 0)
            {
                UnityEngine.Debug.LogError(
                    "CycloneGames.Logger: Unity handoff entries could not be drained during subsystem reset and were explicitly abandoned: "
                    + abandonedEntries + ".");
            }

#if UNITY_EDITOR
            LoggerEditorLinkRegistry.Reset();
            LoggerEditorPathResolver.Reset();
#endif
        }

        internal static void Configure(LoggerProcessingOptions options)
        {
            LoggerProcessingOptions validated = LoggerProcessingOptions.CreateValidated(options);
#if UNITY_EDITOR
            LoggerEditorPathResolver.Configure(
                Application.dataPath,
                Application.platform == RuntimePlatform.WindowsEditor);
#endif
            lock (QueueLock)
            {
                if (_initializationBlocked)
                {
                    throw new InvalidOperationException("Logger runtime initialization is blocked because the previous owner did not stop safely.");
                }

                if (_quitting)
                {
                    throw new InvalidOperationException("Unity logger queue cannot be configured after shutdown has started.");
                }

                if (_count != 0 || _reservedCount != 0 || _inFlightCount != 0)
                {
                    throw new InvalidOperationException("Unity logger queue cannot be reconfigured while it contains messages.");
                }

                _entries = new LogEntry[validated.UnityConsoleMaxQueuedMessages];
                _head = 0;
                _maxQueuedCharacters = validated.UnityConsoleMaxQueuedCharacters;
                _reservedCriticalMessages = Math.Min(validated.ReservedCriticalMessages, _entries.Length - 1);
                _reservedCriticalCharacters = Math.Min(validated.ReservedCriticalCharacters, _maxQueuedCharacters - 1);
                _overflowPolicy = validated.UnityConsoleOverflowPolicy;
                _criticalLevel = validated.CriticalLevel;
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
                throw new InvalidOperationException("Logger runtime initialization is blocked because the previous owner did not stop safely.");
            }

            if (Environment.CurrentManagedThreadId != _mainThreadId)
            {
                throw new InvalidOperationException("LoggerUpdater must be created on the Unity main thread.");
            }

            var gameObject = new GameObject("CycloneGames.Logger.RuntimeHost");
            gameObject.hideFlags = HideFlags.HideAndDontSave;
            DontDestroyOnLoad(gameObject);
            _instance = gameObject.AddComponent<LoggerUpdater>();
            Application.quitting -= OnApplicationQuitting;
            Application.quitting += OnApplicationQuitting;
        }

        internal static bool TryReserve(LogLevel level, int estimatedCharacters, out Reservation reservation)
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
            lock (QueueLock)
            {
                if (_initializationBlocked || _quitStarted || _quitting)
                {
                    throw new InvalidOperationException("Unity logger adapter registration is unavailable while runtime initialization is blocked or shutting down.");
                }

                _adapterCount++;
                return _generation;
            }
        }

        internal static void UnregisterAdapter(int generation)
        {
            lock (QueueLock)
            {
                if (generation == _generation && _adapterCount > 0)
                {
                    _adapterCount--;
                }
            }
        }

        internal static bool Commit(LogLevel level, string message, Reservation reservation)
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

                int characters = message?.Length ?? 0;
                if (characters > _maxQueuedCharacters)
                {
                    RecordDropNoLock(level);
                    return false;
                }

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
                _entries[tail].Level = level;
                _entries[tail].Message = message;
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

        internal static UnityLoggerStatistics GetStatistics()
        {
            lock (QueueLock)
            {
                return new UnityLoggerStatistics(
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

            LoggerUpdater instance = _instance;
            _instance = null;
            if (Environment.CurrentManagedThreadId != _mainThreadId)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(instance.gameObject);
            }
            else
            {
                DestroyImmediate(instance.gameObject);
            }
        }

        private void Update()
        {
            if (CLogger.TryGetInstance(out CLogger logger))
            {
                logger.PumpWithinBudget(DefaultPumpItems, CorePumpBudgetMilliseconds);
            }

            DrainUnityQueue(DefaultPumpItems, UnityConsoleBudgetMilliseconds);
        }

        private void OnApplicationPause(bool paused)
        {
            if (!paused || !CLogger.TryGetInstance(out CLogger logger))
            {
                return;
            }

            logger.TryFlush(LogFlushMode.Buffered, 50);
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
            lock (QueueLock)
            {
                if (_quitStarted)
                {
                    return;
                }

                _quitStarted = true;
            }

            CLogger.ShutdownForApplicationQuit();
            lock (QueueLock)
            {
                _quitting = true;
            }

            DrainUnityQueue(int.MaxValue, 50);
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
                    LogToUnity(entry.Level, entry.Message);
                }
                finally
                {
                    CompleteProcessing(entry.Message?.Length ?? 0);
                }

                processed++;
                if (Stopwatch.GetTimestamp() - start >= budgetTicks)
                {
                    break;
                }
            }
        }

        [HideInCallstack]
        private static void LogToUnity(LogLevel level, string message)
        {
            LogType logType;
            switch (level)
            {
                case LogLevel.Warning:
                    logType = LogType.Warning;
                    break;
                case LogLevel.Error:
                case LogLevel.Fatal:
                    logType = LogType.Error;
                    break;
                default:
                    logType = LogType.Log;
                    break;
            }

            LogFormatArguments[0] = message;
            try
            {
                UnityEngine.Debug.LogFormat(logType, LogOption.NoStacktrace, null, "{0}", LogFormatArguments);
            }
            finally
            {
                LogFormatArguments[0] = null;
            }
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
                int characters = entry.Message?.Length ?? 0;
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

        private static bool HasCapacityNoLock(LogLevel level, int characters)
        {
            bool critical = level >= _criticalLevel;
            int messageLimit = critical ? _entries.Length : _entries.Length - _reservedCriticalMessages;
            int characterLimit = critical ? _maxQueuedCharacters : _maxQueuedCharacters - _reservedCriticalCharacters;
            return _count + _inFlightCount + _reservedCount < messageLimit
                && (long)_queuedCharacters + _inFlightCharacters + _reservedCharacters + characters <= characterLimit;
        }

        private static bool TryEvictNoLock(LogLevel incomingLevel)
        {
            bool incomingCritical = incomingLevel >= _criticalLevel;
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
            RecordDropNoLock(dropped.Level);
            return true;
        }

        private static int FindOldestNormalOffsetNoLock()
        {
            for (int offset = 0; offset < _count; offset++)
            {
                int index = (_head + offset) % _entries.Length;
                if (_entries[index].Level < _criticalLevel)
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
                _queuedCharacters -= removed.Message?.Length ?? 0;
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
            _queuedCharacters -= removed.Message?.Length ?? 0;
            return removed;
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

        private static void RecordDropNoLock(LogLevel level)
        {
            _droppedCount++;
            if (level >= _criticalLevel)
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
                    RecordDropNoLock(dropped.Level);
                }
            }
        }
    }
}
