using System;

namespace CycloneGames.Logger
{
    public interface ILogger : IDisposable
    {
        void LogTrace(LogMessage logMessage);
        void LogDebug(LogMessage logMessage);
        void LogInfo(LogMessage logMessage);
        void LogWarning(LogMessage logMessage);
        void LogError(LogMessage logMessage);
        void LogFatal(LogMessage logMessage);
    }
}
