using System;

namespace CycloneGames.Logging.Pipeline
{
    /// <summary>
    /// Synchronous log sink contract. Emit may run on a background worker when threaded
    /// processing is selected. Implementations must be thread-safe, must return promptly,
    /// and must not retain the borrowed <see cref="LogEvent"/> instance or its builder.
    /// Unity main-thread work must be copied into a bounded main-thread-owned queue.
    /// </summary>
    public interface ILogSink : IDisposable
    {
        void Emit(LogEvent logEvent);
    }
}
