using System;

namespace CycloneGames.Logger
{
    public enum FileMaintenanceMode
    {
        None = 0,
        WarnOnly = 1,
        Rotate = 2
    }

    public enum LogSourcePathMode : byte
    {
        FileName = 0,
        None = 1,
        FullPath = 2
    }

    /// <summary>
    /// Configures the bounded file sink. Values are copied and validated by <see cref="FileLogger"/>.
    /// </summary>
    public sealed class FileLoggerOptions
    {
        /// <summary>Controls whether file growth is ignored, observed, or bounded by rotation.</summary>
        public FileMaintenanceMode MaintenanceMode = FileMaintenanceMode.Rotate;

        /// <summary>Maximum UTF-8 byte length of the active file when rotation is enabled.</summary>
        public long MaxFileBytes = 10L * 1024L * 1024L;

        /// <summary>Maximum number of archives owned by this sink. Zero deletes archives after rotation.</summary>
        public int MaxArchiveFiles = 5;

        /// <summary>Number of accepted records between buffered flushes.</summary>
        public int FlushBatchSize = 64;

        /// <summary>Maximum buffered flush interval in milliseconds. Zero flushes every record.</summary>
        public int FlushIntervalMs = 1000;

        /// <summary>Minimum interval in milliseconds between recovery attempts after an open failure.</summary>
        public int RecoveryRetryIntervalMs = 5000;

        /// <summary>Minimum interval in milliseconds between emergency diagnostics. Zero disables throttling.</summary>
        public int DiagnosticIntervalMs = 30000;

        /// <summary>Requests a durable operating-system flush for Fatal records.</summary>
        public bool DurableFlushOnFatal;

        /// <summary>Controls source path disclosure. The privacy-preserving default writes only the file name.</summary>
        public LogSourcePathMode SourcePathMode = LogSourcePathMode.FileName;

        /// <summary>
        /// Returns an independent options instance so callers cannot mutate shared process state.
        /// </summary>
        public static FileLoggerOptions Default
        {
            get { return new FileLoggerOptions(); }
        }

        public FileLoggerOptions()
        {
        }

        public FileLoggerOptions(FileLoggerOptions source)
        {
            if (source == null)
            {
                return;
            }

            MaintenanceMode = source.MaintenanceMode;
            MaxFileBytes = source.MaxFileBytes;
            MaxArchiveFiles = source.MaxArchiveFiles;
            FlushBatchSize = source.FlushBatchSize;
            FlushIntervalMs = source.FlushIntervalMs;
            RecoveryRetryIntervalMs = source.RecoveryRetryIntervalMs;
            DiagnosticIntervalMs = source.DiagnosticIntervalMs;
            DurableFlushOnFatal = source.DurableFlushOnFatal;
            SourcePathMode = source.SourcePathMode;
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
            if (!Enum.IsDefined(typeof(FileMaintenanceMode), MaintenanceMode))
            {
                throw new ArgumentOutOfRangeException(nameof(MaintenanceMode), "Unknown maintenance mode.");
            }

            if (MaxFileBytes < 1L)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxFileBytes), "MaxFileBytes must be greater than zero.");
            }

            if (MaxArchiveFiles < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(MaxArchiveFiles), "MaxArchiveFiles cannot be negative.");
            }

            if (FlushBatchSize < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(FlushBatchSize), "FlushBatchSize must be greater than zero.");
            }

            if (FlushIntervalMs < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(FlushIntervalMs), "FlushIntervalMs cannot be negative.");
            }

            if (RecoveryRetryIntervalMs < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(RecoveryRetryIntervalMs), "RecoveryRetryIntervalMs cannot be negative.");
            }

            if (DiagnosticIntervalMs < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(DiagnosticIntervalMs), "DiagnosticIntervalMs cannot be negative.");
            }

            if (!Enum.IsDefined(typeof(LogSourcePathMode), SourcePathMode))
            {
                throw new ArgumentOutOfRangeException(nameof(SourcePathMode), "Unknown source path mode.");
            }

        }
    }
}
