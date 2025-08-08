using System.Collections.Concurrent;

namespace CycloneGames.Logger
{
    /// <summary>
    /// A thread-safe object pool for LogMessage instances.
    /// This is a core component for achieving zero GC logging during runtime.
    /// </summary>
    public static class LogMessagePool
    {
        // Using ConcurrentBag for thread-safe, lock-free pooling.
        // It's optimized for scenarios where the same thread both produces and consumes items.
        private static readonly ConcurrentBag<LogMessage> Pool = new ConcurrentBag<LogMessage>();

        /// <summary>
        /// Retrieves a LogMessage object from the pool.
        /// If the pool is empty, a new instance is created.
        /// </summary>
        /// <returns>An initialized LogMessage object.</returns>
        public static LogMessage Get()
        {
            if (Pool.TryTake(out var message))
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
                Pool.Add(message);
            }
        }
    }
}
