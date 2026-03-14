using System;

namespace CycloneGames.Logger
{
    public interface ILogger : IDisposable
    {
        void Log(LogMessage logMessage);
    }
}
