using CycloneGames.Logging;

namespace CycloneGames.Logging.Unity
{
    public enum LoggingInitializationStatus : byte
    {
        Initialized = 0,
        AlreadyInitialized = 1,
        NoSinksConfigured = 2,
        ShutdownFailed = 3,
        ExistingProcessWriterNotOwned = 4
    }

    public readonly struct LoggingInitializationResult
    {
        public readonly LoggingInitializationStatus Status;
        public readonly bool ProcessWriterInstalled;

        public bool IsInitialized => Status == LoggingInitializationStatus.Initialized
            || Status == LoggingInitializationStatus.AlreadyInitialized;

        internal LoggingInitializationResult(
            LoggingInitializationStatus status,
            bool processWriterInstalled)
        {
            Status = status;
            ProcessWriterInstalled = processWriterInstalled;
        }
    }

    public readonly struct LoggingReinitializationResult
    {
        public readonly LogPipelineShutdownResult Shutdown;
        public readonly LoggingInitializationResult Initialization;

        public bool Succeeded =>
            (Shutdown.IsComplete || Shutdown.Status == LogPipelineShutdownStatus.NotStarted)
            && Initialization.IsInitialized;

        internal LoggingReinitializationResult(
            LogPipelineShutdownResult shutdown,
            LoggingInitializationResult initialization)
        {
            Shutdown = shutdown;
            Initialization = initialization;
        }
    }
}
