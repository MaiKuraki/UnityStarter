namespace CycloneGames.Logger
{
    public enum FileMaintenanceMode
    {
        None = 0,
        WarnOnly = 1,
        Rotate = 2
    }

    public sealed class FileLoggerOptions
    {
        public FileMaintenanceMode MaintenanceMode = FileMaintenanceMode.WarnOnly;
        public long MaxFileBytes = 10L * 1024L * 1024L; // 10 MB
        public int MaxArchiveFiles = 5;
        public string ArchiveTimestampFormat = "yyyyMMdd_HHmmss";

        // Flush strategy: batch writes for I/O throughput, flush immediately on Error/Fatal.
        public int FlushBatchSize = 64;
        public int FlushIntervalMs = 1000;

        public static readonly FileLoggerOptions Default = new FileLoggerOptions();
    }
}