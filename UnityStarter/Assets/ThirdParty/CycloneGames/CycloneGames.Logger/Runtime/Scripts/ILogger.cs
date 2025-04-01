using System;

namespace CycloneGames.Logger
{
    public interface ILogger : IDisposable
    {
        void LogTrace(string message);
        void LogDebug(string message);
        void LogInfo(string message);
        void LogWarning(string message);
        void LogError(string message);
        void LogFatal(string message);
    }
}