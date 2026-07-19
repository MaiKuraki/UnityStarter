namespace CycloneGames.Logger
{
    public enum LogFlushMode : byte
    {
        Buffered = 0,
        Durable = 1
    }

    public enum LoggerShutdownStatus : byte
    {
        NotStarted = 0,
        Completed = 1,
        CompletedWithDrops = 2,
        CompletedWithFailures = 3,
        TimedOut = 4,
        AlreadyStopped = 5
    }

    public readonly struct LoggerShutdownResult
    {
        public readonly LoggerShutdownStatus Status;
        public readonly long DroppedMessageCount;
        public readonly bool SinksFlushed;

        public bool IsComplete => Status == LoggerShutdownStatus.Completed
            || Status == LoggerShutdownStatus.CompletedWithDrops
            || Status == LoggerShutdownStatus.CompletedWithFailures
            || Status == LoggerShutdownStatus.AlreadyStopped;

        public LoggerShutdownResult(LoggerShutdownStatus status, long droppedMessageCount, bool sinksFlushed)
        {
            Status = status;
            DroppedMessageCount = droppedMessageCount;
            SinksFlushed = sinksFlushed;
        }
    }

    /// <summary>
    /// Optional sink capability used by explicit logger flush and shutdown operations.
    /// </summary>
    public interface IFlushableLogger
    {
        bool TryFlush(LogFlushMode mode);
    }

    /// <summary>
    /// Optional capability declaring that repeated <see cref="System.IDisposable.Dispose"/>
    /// calls are safe after a previous disposal attempt threw.
    /// </summary>
    public interface IIdempotentLoggerSinkDisposal
    {
    }

    internal interface IMaintainableLogger
    {
        void PerformMaintenance();
    }
}
