namespace CycloneGames.Logger
{
    public enum FileMaintenanceMode
    {
        None = 0,
        WarnOnly = 1,
        Rotate = 2
    }

    /// <summary>
    /// Options for FileLogger maintenance and rotation.
    /// </summary>
    public sealed class FileLoggerOptions
    {
        public FileMaintenanceMode MaintenanceMode = FileMaintenanceMode.WarnOnly;
        public long MaxFileBytes = 10L * 1024L * 1024L; // 10 MB
        public int MaxArchiveFiles = 5;                 // keep latest N archives
        public string ArchiveTimestampFormat = "yyyyMMdd_HHmmss";

        public static readonly FileLoggerOptions Default = new FileLoggerOptions();
    }
}


