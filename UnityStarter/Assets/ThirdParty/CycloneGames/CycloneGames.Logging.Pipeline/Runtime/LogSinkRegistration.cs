namespace CycloneGames.Logging.Pipeline
{
    /// <summary>
    /// Controls whether a pipeline accepts multiple active sinks of the same concrete type.
    /// </summary>
    public enum LogSinkRegistrationMode : byte
    {
        AllowMultiple = 0,
        UniqueExactType = 1
    }

    public enum LogSinkRegistrationStatus : byte
    {
        NotAttempted = 0,
        Registered = 1,
        AlreadyRegistered = 2,
        AlreadyOwnedByPipeline = 3,
        RejectedDuplicateType = 4,
        RejectedCapacity = 5,
        RejectedPipelineStopping = 6
    }

    /// <summary>
    /// Describes both the registration outcome and the sink ownership boundary.
    /// A sink is never disposed as a side effect of a rejected registration.
    /// </summary>
    public readonly struct LogSinkRegistrationResult
    {
        internal LogSinkRegistrationResult(LogSinkRegistrationStatus status)
        {
            Status = status;
        }

        public LogSinkRegistrationStatus Status { get; }

        public bool IsRegistered => Status == LogSinkRegistrationStatus.Registered
            || Status == LogSinkRegistrationStatus.AlreadyRegistered;

        public bool PipelineOwnsSink => IsRegistered
            || Status == LogSinkRegistrationStatus.AlreadyOwnedByPipeline;

        public bool CallerRetainsOwnership => !PipelineOwnsSink;
    }
}
