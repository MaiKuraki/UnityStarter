using System;
using System.Threading;
using UnityEngine;

namespace CycloneGames.DataTable.Unity
{
    /// <summary>
    /// Bridges DataTable logging to Unity's Debug.Log* on the captured Unity main thread.
    /// Calls from another thread use <see cref="Console"/> so this adapter does not invoke a
    /// Unity API outside its declared thread affinity.
    /// <para>
    /// If you prefer a different logger (e.g. CycloneGames.Logger, Serilog, custom),
    /// set <see cref="DataTableLogger"/> delegates in your own initialization code after
    /// subsystem registration.
    /// </para>
    /// <para>
    /// To use your own logger:
    /// <code>
    /// // In your game initializer (runs after SubsystemRegistration):
    /// DataTableLogger.LogWarning = msg => MyLogger.Warn(msg);
    /// DataTableLogger.LogError   = msg => MyLogger.Error(msg);
    /// DataTableLogger.LogInfo    = msg => MyLogger.Info(msg);
    /// </code>
    /// You can also delete this bootstrap file entirely if you never want Unity logging.
    /// </para>
    /// </summary>
    public static class DataTableUnityBootstrap
    {
        private static readonly Action<string> WarningLogger = LogWarning;
        private static readonly Action<string> ErrorLogger = LogError;
        private static readonly Action<string> InfoLogger = LogInfo;
        private static int _unityMainThreadId;

#if UNITY_5_3_OR_NEWER
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void InjectLoggerDelegates()
        {
            // SubsystemRegistration also runs when Enter Play Mode has domain reload disabled.
            // Reassign every delegate so a previous play session cannot leak logger state.
            Volatile.Write(ref _unityMainThreadId, Thread.CurrentThread.ManagedThreadId);
            DataTableLogger.ResetToDefaults();
            DataTableLogger.LogWarning = WarningLogger;
            DataTableLogger.LogError = ErrorLogger;
            DataTableLogger.LogInfo = InfoLogger;
        }
#endif

        private static void LogWarning(string message)
        {
            if (IsUnityMainThread())
            {
                Debug.LogWarning(message);
                return;
            }

            Console.Error.WriteLine(message);
        }

        private static void LogError(string message)
        {
            if (IsUnityMainThread())
            {
                Debug.LogError(message);
                return;
            }

            Console.Error.WriteLine(message);
        }

        private static void LogInfo(string message)
        {
            if (IsUnityMainThread())
            {
                Debug.Log(message);
                return;
            }

            Console.WriteLine(message);
        }

        private static bool IsUnityMainThread()
        {
            int mainThreadId = Volatile.Read(ref _unityMainThreadId);
            return mainThreadId != 0 &&
                   Thread.CurrentThread.ManagedThreadId == mainThreadId;
        }
    }
}
