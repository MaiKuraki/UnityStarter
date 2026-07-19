using System;

namespace CycloneGames.Logger
{
    /// <summary>
    /// Synchronous log sink contract. Log may run on a background worker when threaded
    /// processing is selected. Implementations must be thread-safe, must return promptly,
    /// and must not retain the borrowed <see cref="LogMessage"/> instance or its builder.
    /// Unity main-thread work must be copied into a bounded main-thread-owned queue.
    /// </summary>
    public interface ILogger : IDisposable
    {
        void Log(LogMessage logMessage);
    }
}
