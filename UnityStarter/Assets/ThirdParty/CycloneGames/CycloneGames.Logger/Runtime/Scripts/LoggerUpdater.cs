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
        private static readonly ConcurrentQueue<(LogLevel level, string message)> UnityLogQueue = new();

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
            UnityLogQueue.Enqueue((level, message));
        }

        private void Update()
        {
            CLogger.Instance.Pump();

            long startTime = System.Diagnostics.Stopwatch.GetTimestamp();
            long budgetTicks = System.Diagnostics.Stopwatch.Frequency / 500;

            while (UnityLogQueue.TryDequeue(out var item))
            {
                switch (item.level)
                {
                    case LogLevel.Trace:
                    case LogLevel.Debug:
                    case LogLevel.Info:
                        UnityEngine.Debug.Log(item.message);
                        break;
                    case LogLevel.Warning:
                        UnityEngine.Debug.LogWarning(item.message);
                        break;
                    case LogLevel.Error:
                    case LogLevel.Fatal:
                        UnityEngine.Debug.LogError(item.message);
                        break;
                }

                if (System.Diagnostics.Stopwatch.GetTimestamp() - startTime > budgetTicks)
                {
                    break; // Defer remaining logs to next frame
                }
            }
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}