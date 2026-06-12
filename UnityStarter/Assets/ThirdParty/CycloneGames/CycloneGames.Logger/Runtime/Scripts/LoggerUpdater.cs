using System;
using System.Collections.Concurrent;
using System.Threading;
using UnityEngine;

namespace CycloneGames.Logger
{
    /// <summary>
    /// Handles main-thread updates for the logging system.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    internal sealed class LoggerUpdater : MonoBehaviour
    {
        private static LoggerUpdater _instance;
        private static int _mainThreadId = Environment.CurrentManagedThreadId;
        private static readonly ConcurrentQueue<LogEntry> UnityLogQueue = new();
        private static int _unityLogQueueCount;
        private static int _maxQueuedUnityLogs = LoggerProcessingOptions.DefaultUnityConsoleMaxQueuedMessages;
        private static LogQueueOverflowPolicy _overflowPolicy = LogQueueOverflowPolicy.DropNewest;
        private static LogLevel _guaranteedLevel = LogLevel.Error;
        private static long _droppedUnityLogCount;

        // Reuse to avoid params object[] allocation in Debug.LogFormat; safe because LogToUnity runs on main thread only.
        private static readonly object[] _logFormatArgs = new object[1];

        internal struct LogEntry
        {
            public LogLevel Level;
            public string Message;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            _instance = null;
            _mainThreadId = Environment.CurrentManagedThreadId;
            Volatile.Write(ref _unityLogQueueCount, 0);
            Interlocked.Exchange(ref _droppedUnityLogCount, 0);
            while (UnityLogQueue.TryDequeue(out _))
            {
            }
        }

        internal static void Configure(LoggerProcessingOptions options)
        {
            options = LoggerProcessingOptions.CreateValidated(options);
            Volatile.Write(ref _maxQueuedUnityLogs, options.UnityConsoleMaxQueuedMessages);
            _overflowPolicy = options.OverflowPolicy;
            _guaranteedLevel = options.GuaranteedLevel;
        }

        internal static void EnsureInstance()
        {
            if (_instance != null) return;
            if (Environment.CurrentManagedThreadId != _mainThreadId)
            {
                Console.Error.WriteLine("[WARNING] LoggerUpdater: EnsureInstance must run on Unity main thread.");
                return;
            }

            var go = new GameObject("CycloneGames.Logger.Updater");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            _instance = go.AddComponent<LoggerUpdater>();
        }

        internal static void EnqueueUnityLog(LogLevel level, string message)
        {
            if (!TryReserveUnityLogSlot(level))
            {
                Interlocked.Increment(ref _droppedUnityLogCount);
                return;
            }

            UnityLogQueue.Enqueue(new LogEntry
            {
                Level = level,
                Message = message
            });
        }

        internal static long GetDroppedUnityLogCount()
        {
            return Interlocked.Read(ref _droppedUnityLogCount);
        }

        internal static void Shutdown()
        {
            while (UnityLogQueue.TryDequeue(out _))
            {
                Interlocked.Decrement(ref _unityLogQueueCount);
            }

            if (_instance == null) return;

            var instance = _instance;
            _instance = null;

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
            if (CLogger.TryGetInstance(out var logger))
            {
                logger.Pump();
            }

            long startTime = System.Diagnostics.Stopwatch.GetTimestamp();
            long budgetTicks = System.Diagnostics.Stopwatch.Frequency / 500;

            while (UnityLogQueue.TryDequeue(out var item))
            {
                Interlocked.Decrement(ref _unityLogQueueCount);
                LogToUnity(item);

                if (System.Diagnostics.Stopwatch.GetTimestamp() - startTime > budgetTicks)
                {
                    break;
                }
            }
        }

        private static void LogToUnity(LogEntry entry)
        {
            LogToUnity(entry.Level, entry.Message, true);
        }

        [HideInCallstack]
        internal static void LogToUnity(LogLevel level, string message, bool noStacktrace)
        {
            var logType = level switch
            {
                LogLevel.Warning => UnityEngine.LogType.Warning,
                LogLevel.Error or LogLevel.Fatal => UnityEngine.LogType.Error,
                _ => UnityEngine.LogType.Log
            };

            // Pass pre-allocated object[] to avoid params array allocation.
            _logFormatArgs[0] = message;
            Debug.LogFormat(logType, noStacktrace ? LogOption.NoStacktrace : LogOption.None, null, "{0}", _logFormatArgs);
            _logFormatArgs[0] = null; // Release string reference
        }

        private static bool TryReserveUnityLogSlot(LogLevel level)
        {
            if (TryIncrementUnityLogCount()) return true;

            if (level >= _guaranteedLevel && TryDropOldestUnityLog())
            {
                return TryIncrementUnityLogCount();
            }

            if (_overflowPolicy == LogQueueOverflowPolicy.DropOldest && TryDropOldestUnityLog())
            {
                return TryIncrementUnityLogCount();
            }

            return false;
        }

        private static bool TryIncrementUnityLogCount()
        {
            int maxQueuedLogs = Volatile.Read(ref _maxQueuedUnityLogs);
            while (true)
            {
                int current = Volatile.Read(ref _unityLogQueueCount);
                if (current >= maxQueuedLogs) return false;
                if (Interlocked.CompareExchange(ref _unityLogQueueCount, current + 1, current) == current) return true;
            }
        }

        private static bool TryDropOldestUnityLog()
        {
            if (!UnityLogQueue.TryDequeue(out _)) return false;

            Interlocked.Decrement(ref _unityLogQueueCount);
            Interlocked.Increment(ref _droppedUnityLogCount);
            return true;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}
