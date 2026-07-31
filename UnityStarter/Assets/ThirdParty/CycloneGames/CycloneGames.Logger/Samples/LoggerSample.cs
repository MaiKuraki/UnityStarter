using CycloneGames.Logging;
using UnityEngine;

/// <summary>
/// Minimal use of the project-owned LoggerBootstrap configuration.
/// </summary>
public sealed class LoggerSample : MonoBehaviour
{
    private static readonly LogChannel Log = LoggerSamplesLog.Channel;

    private void Start()
    {
        Log.Info("Logger sample started.");
        Log.Warning("This is a warning example.");
        Log.Error("This is an error example.");
    }
}
