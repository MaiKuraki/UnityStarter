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

        internal struct LogEntry
        {
            public LogLevel Level;
            public string Message;
            public string FilePath;
            public int LineNumber;
        }

        internal static void EnsureInstance()
        {
            if (_instance != null) return;

            var go = new GameObject("CycloneGames.Logger.Updater");
            DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            _instance = go.AddComponent<LoggerUpdater>();
        }

        internal static void EnqueueUnityLog(LogLevel level, string message, string filePath, int lineNumber)
        {
            UnityLogQueue.Enqueue(new LogEntry
            {
                Level = level,
                Message = message,
                FilePath = filePath,
                LineNumber = lineNumber
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
            // Use ILogger.Log with custom context to enable double-click navigation.
            // Format message with embedded location for hyperlink support.
            var logType = entry.Level switch
            {
                LogLevel.Warning => UnityEngine.LogType.Warning,
                LogLevel.Error or LogLevel.Fatal => UnityEngine.LogType.Error,
                _ => UnityEngine.LogType.Log
            };

            // LogFormat with NoStacktrace prevents Unity's auto stack trace.
            // The message already contains clickable hyperlink and source location.
            Debug.LogFormat(logType, LogOption.NoStacktrace, null, "{0}", entry.Message);
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}