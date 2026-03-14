using System.Collections.Concurrent;
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
        private static readonly ConcurrentQueue<LogEntry> UnityLogQueue = new();

        // Reuse to avoid params object[] allocation in Debug.LogFormat; safe because LogToUnity runs on main thread only.
        private static readonly object[] _logFormatArgs = new object[1];

        internal struct LogEntry
        {
            public LogLevel Level;
            public string Message;
        }

        internal static void EnsureInstance()
        {
            if (_instance != null) return;

            var go = new GameObject("CycloneGames.Logger.Updater");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            _instance = go.AddComponent<LoggerUpdater>();
        }

        internal static void EnqueueUnityLog(LogLevel level, string message)
        {
            UnityLogQueue.Enqueue(new LogEntry
            {
                Level = level,
                Message = message
            });
        }

        private void Update()
        {
            CLogger.Instance.Pump();

            long startTime = System.Diagnostics.Stopwatch.GetTimestamp();
            long budgetTicks = System.Diagnostics.Stopwatch.Frequency / 500;

            while (UnityLogQueue.TryDequeue(out var item))
            {
                LogToUnity(item);

                if (System.Diagnostics.Stopwatch.GetTimestamp() - startTime > budgetTicks)
                {
                    break;
                }
            }
        }

        private static void LogToUnity(LogEntry entry)
        {
            var logType = entry.Level switch
            {
                LogLevel.Warning => UnityEngine.LogType.Warning,
                LogLevel.Error or LogLevel.Fatal => UnityEngine.LogType.Error,
                _ => UnityEngine.LogType.Log
            };

            // Pass pre-allocated object[] to avoid params array allocation.
            _logFormatArgs[0] = entry.Message;
            Debug.LogFormat(logType, LogOption.NoStacktrace, null, "{0}", _logFormatArgs);
            _logFormatArgs[0] = null; // Release string reference
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}