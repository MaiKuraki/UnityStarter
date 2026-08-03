namespace CycloneGames.Logging.Pipeline
{
    public enum LogFlushMode : byte
    {
        Buffered = 0,
        Durable = 1
    }

    public enum LogPipelineShutdownStatus : byte
    {
        NotStarted = 0,
        Completed = 1,
        CompletedWithDrops = 2,
        CompletedWithFailures = 3,
        TimedOut = 4,
        InProgress = 5
    }

    public readonly struct LogPipelineShutdownResult
    {
        public readonly LogPipelineShutdownStatus Status;
        public readonly long DroppedMessageCount;
        public readonly bool SinksFlushed;

        public bool IsComplete => Status == LogPipelineShutdownStatus.Completed
            || Status == LogPipelineShutdownStatus.CompletedWithDrops
            || Status == LogPipelineShutdownStatus.CompletedWithFailures;

        public LogPipelineShutdownResult(LogPipelineShutdownStatus status, long droppedMessageCount, bool sinksFlushed)
        {
            Status = status;
            DroppedMessageCount = droppedMessageCount;
            SinksFlushed = sinksFlushed;
        }
    }

    /// <summary>
    /// Optional sink capability used by explicit pipeline flush and shutdown operations.
    /// </summary>
    public interface IFlushableLogSink
    {
        bool TryFlush(LogFlushMode mode);
    }

    /// <summary>
    /// Optional capability declaring that repeated <see cref="System.IDisposable.Dispose"/>
    /// calls are safe after a previous disposal attempt threw.
    /// </summary>
    public interface IIdempotentLogSinkDisposal
    {
    }

    internal interface IMaintainableLogSink
    {
        void PerformMaintenance();
    }
}
