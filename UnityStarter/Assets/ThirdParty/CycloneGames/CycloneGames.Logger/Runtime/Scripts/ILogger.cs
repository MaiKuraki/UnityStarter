using System;

namespace CycloneGames.Logger
{
    public interface ILogger : IDisposable
    {
        void LogTrace(in LogMessage logMessage);
        void LogDebug(in LogMessage logMessage);
        void LogInfo(in LogMessage logMessage);
        void LogWarning(in LogMessage logMessage);
        void LogError(in LogMessage logMessage);
        void LogFatal(in LogMessage logMessage);
    }
}