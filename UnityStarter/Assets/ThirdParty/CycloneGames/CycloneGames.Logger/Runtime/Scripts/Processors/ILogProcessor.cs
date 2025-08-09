using System;

namespace CycloneGames.Logger
{
    /// <summary>
    /// Abstracts the log message processing strategy to decouple platform specifics
    /// (e.g., background threads not available on some platforms) from the core logger.
    /// </summary>
    public interface ILogProcessor : IDisposable
    {
        void Enqueue(LogMessage message);

        /// <summary>
        /// Process queued messages; for threaded strategies this can be a no-op.
        /// </summary>
        /// <param name="maxItems">Maximum items to process in this call.</param>
        void Pump(int maxItems);
    }
}


