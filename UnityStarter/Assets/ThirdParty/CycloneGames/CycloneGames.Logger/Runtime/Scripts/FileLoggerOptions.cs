namespace CycloneGames.Logger
{
    using System;

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

        public FileLoggerOptions()
        {
        }

        public FileLoggerOptions(FileLoggerOptions source)
        {
            if (source == null) source = Default;

            MaintenanceMode = source.MaintenanceMode;
            MaxFileBytes = source.MaxFileBytes;
            MaxArchiveFiles = source.MaxArchiveFiles;
            ArchiveTimestampFormat = source.ArchiveTimestampFormat;
            FlushBatchSize = source.FlushBatchSize;
            FlushIntervalMs = source.FlushIntervalMs;
        }

        public FileLoggerOptions Clone()
        {
            return new FileLoggerOptions(this);
        }

        internal static FileLoggerOptions CreateValidated(FileLoggerOptions source)
        {
            var options = new FileLoggerOptions(source);
            options.Validate();
            return options;
        }

        internal void Validate()
        {
            if (!Enum.IsDefined(typeof(FileMaintenanceMode), MaintenanceMode)) throw new ArgumentOutOfRangeException(nameof(MaintenanceMode), "Unknown maintenance mode.");
            if (MaxFileBytes < 1L) throw new ArgumentOutOfRangeException(nameof(MaxFileBytes), "MaxFileBytes must be greater than zero.");
            if (MaxArchiveFiles < 0) throw new ArgumentOutOfRangeException(nameof(MaxArchiveFiles), "MaxArchiveFiles cannot be negative.");
            if (MaintenanceMode == FileMaintenanceMode.Rotate && string.IsNullOrEmpty(ArchiveTimestampFormat)) throw new ArgumentException("ArchiveTimestampFormat is required when rotation is enabled.", nameof(ArchiveTimestampFormat));
            if (FlushBatchSize < 1) throw new ArgumentOutOfRangeException(nameof(FlushBatchSize), "FlushBatchSize must be greater than zero.");
            if (FlushIntervalMs < 0) throw new ArgumentOutOfRangeException(nameof(FlushIntervalMs), "FlushIntervalMs cannot be negative.");
        }
    }
}
