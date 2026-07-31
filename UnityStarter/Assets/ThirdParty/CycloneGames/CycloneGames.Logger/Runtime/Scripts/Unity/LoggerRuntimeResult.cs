namespace CycloneGames.Logger
{
    public enum LoggerInitializationStatus : byte
    {
        Initialized = 0,
        AlreadyInitialized = 1,
        ExistingLoggerNotOwned = 2,
        NoSinksConfigured = 3,
        ShutdownFailed = 4,
        ExistingProcessWriterNotOwned = 5
    }

    public readonly struct LoggerInitializationResult
    {
        public readonly LoggerInitializationStatus Status;
        public readonly bool ProcessWriterInstalled;

        public bool IsInitialized => Status == LoggerInitializationStatus.Initialized
            || Status == LoggerInitializationStatus.AlreadyInitialized
            || Status == LoggerInitializationStatus.NoSinksConfigured;

        internal LoggerInitializationResult(
            LoggerInitializationStatus status,
            bool processWriterInstalled)
        {
            Status = status;
            ProcessWriterInstalled = processWriterInstalled;
        }
    }

    public readonly struct LoggerReinitializationResult
    {
        public readonly LoggerShutdownResult Shutdown;
        public readonly LoggerInitializationResult Initialization;

        public bool Succeeded =>
            (Shutdown.IsComplete || Shutdown.Status == LoggerShutdownStatus.NotStarted)
            && Initialization.IsInitialized;

        internal LoggerReinitializationResult(
            LoggerShutdownResult shutdown,
            LoggerInitializationResult initialization)
        {
            Shutdown = shutdown;
            Initialization = initialization;
        }
    }
}
