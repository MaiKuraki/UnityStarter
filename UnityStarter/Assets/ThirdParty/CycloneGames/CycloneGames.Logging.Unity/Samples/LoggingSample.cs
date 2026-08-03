using CycloneGames.Logging;
using UnityEngine;

namespace CycloneGames.Logging.Unity.Samples
{
    /// <summary>
    /// Minimal use of the project-owned LoggingBootstrap configuration.
    /// </summary>
    public sealed class LoggingSample : MonoBehaviour
    {
        private static readonly LogChannel Log = LoggingSamplesLog.Channel;

        private void Start()
        {
            Log.Info("Logging sample started.");
            Log.Warning("This is a warning example.");
            Log.Error("This is an error example.");
        }
    }
}
