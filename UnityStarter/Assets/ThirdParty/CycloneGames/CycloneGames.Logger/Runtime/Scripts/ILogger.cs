using System;

namespace CycloneGames.Logger
{
    public interface ILogger : IDisposable
    {
        void LogTrace(in string message);
        void LogDebug(in string message);
        void LogInfo(in string message);
        void LogWarning(in string message);
        void LogError(in string message);
        void LogFatal(in string message);
    }
}