using System.Collections.Concurrent;

namespace CycloneGames.Logger
{
    /// <summary>
    /// A thread-safe object pool for LogMessage instances.
    /// This is a core component for achieving zero GC logging during runtime.
    /// </summary>
    public static class LogMessagePool
    {
        private static readonly ConcurrentQueue<LogMessage> Pool = new ConcurrentQueue<LogMessage>();

        /// <summary>
        /// Retrieves a LogMessage object from the pool.
        /// If the pool is empty, a new instance is created.
        /// </summary>
        /// <returns>An initialized LogMessage object.</returns>
        public static LogMessage Get()
        {
            if (Pool.TryDequeue(out var message))
            {
                return message;
            }
            return new LogMessage();
        }

        /// <summary>
        /// Returns a LogMessage object to the pool after resetting its state.
        /// </summary>
        /// <param name="message">The LogMessage object to return.</param>
        public static void Return(LogMessage message)
        {
            if (message != null)
            {
                message.Reset();
                Pool.Enqueue(message);
            }
        }
    }
}