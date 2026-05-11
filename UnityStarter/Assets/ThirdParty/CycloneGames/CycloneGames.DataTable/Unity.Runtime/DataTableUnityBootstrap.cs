using UnityEngine;

namespace CycloneGames.DataTable.Unity
{
    /// <summary>
    /// Bridges DataTable logging to Unity's Debug.Log* at startup.
    /// <para>
    /// If you prefer a different logger (e.g. CycloneGames.Logger, Serilog, custom),
    /// just set <see cref="DataTableLogger"/> delegates in your own initialization code.
    /// The bootstrap only activates when the delegates are still at their Core defaults.
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
#if UNITY_5_3_OR_NEWER
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void InjectLoggerDelegates()
        {
            if (!DataTableLogger.IsDefault)
            {
                return;
            }

            DataTableLogger.LogWarning = msg => Debug.LogWarning(msg);
            DataTableLogger.LogError = msg => Debug.LogError(msg);
            DataTableLogger.LogInfo = msg => Debug.Log(msg);
        }
#endif
    }
}
