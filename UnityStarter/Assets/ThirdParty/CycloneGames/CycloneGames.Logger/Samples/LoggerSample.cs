using CycloneGames.Logger;
using UnityEngine;

/// <summary>
/// Minimal use of the project-owned LoggerBootstrap configuration.
/// </summary>
public sealed class LoggerSample : MonoBehaviour
{
    private void Start()
    {
        CLogger.LogInfo("Logger sample started.", "Sample");
        CLogger.LogWarning("This is a warning example.", "Sample");
        CLogger.LogError("This is an error example.", "Sample");
    }
}
