using UnityEngine;

namespace CycloneGames.Logger
{
public class UnityLogger : ILogger
{
    public void LogTrace(string message)
    {
        Debug.Log($"TRACE: {message}");
    }

    public void LogDebug(string message)
    {
        Debug.Log($"DEBUG: {message}");
    }

    public void LogInfo(string message)
    {
        Debug.Log(message);
    }

    public void LogWarning(string message)
    {
        Debug.LogWarning(message);
    }

    public void LogError(string message)
    {
        Debug.LogError(message);
    }

    public void LogFatal(string message)
    {
        Debug.LogError($"FATAL: {message}");
    }
}
}