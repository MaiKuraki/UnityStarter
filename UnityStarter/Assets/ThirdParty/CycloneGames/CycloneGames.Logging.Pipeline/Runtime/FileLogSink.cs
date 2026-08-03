using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using CycloneGames.Logging;
using CycloneGames.Logging.Pipeline.Internal;

namespace CycloneGames.Logging.Pipeline
{
    public enum FileLogSinkHealth : byte
    {
        Healthy = 0,
        Degraded = 1,
        Faulted = 2,
        Disposed = 3
    }

    public enum FileLogSinkFailureKind : byte
    {
        None = 0,
        Formatting = 1,
        Write = 2,
        Flush = 3,
        DurableFlush = 4,
        Rotation = 5,
        ArchiveCleanup = 6,
        Recovery = 7,
        Dispose = 8
    }

    /// <summary>
    /// Immutable health and throughput snapshot for a <see cref="FileLogSink"/> instance.
    /// Counts are lifetime totals and are safe to read from any thread.
    /// </summary>
    public readonly struct FileLogSinkStatistics
    {
        public long AttemptedEntries { get; }
        public long WrittenEntries { get; }
        public long DroppedEntries { get; }
        public long WriteFailures { get; }
        public long FlushFailures { get; }
        public long RotationCount { get; }
        public long RotationFailures { get; }
        public long ArchiveCleanupFailures { get; }
        public long ArchiveEntriesInspected { get; }
        public long ArchiveFilesDeleted { get; }
        public bool ArchiveCleanupPending { get; }
        public long RecoveryCount { get; }
        public long RecoveryFailures { get; }
        public long SuppressedDiagnostics { get; }
        public long CurrentFileBytes { get; }
        public FileLogSinkHealth Health { get; }
        public FileLogSinkFailureKind LastFailure { get; }
        public DateTime LastFailureUtc { get; }

        internal FileLogSinkStatistics(
            long attemptedEntries,
            long writtenEntries,
            long droppedEntries,
            long writeFailures,
            long flushFailures,
            long rotationCount,
            long rotationFailures,
            long archiveCleanupFailures,
            long archiveEntriesInspected,
            long archiveFilesDeleted,
            bool archiveCleanupPending,
            long recoveryCount,
            long recoveryFailures,
            long suppressedDiagnostics,
            long currentFileBytes,
            FileLogSinkHealth health,
            FileLogSinkFailureKind lastFailure,
            DateTime lastFailureUtc)
        {
            AttemptedEntries = attemptedEntries;
            WrittenEntries = writtenEntries;
            DroppedEntries = droppedEntries;
            WriteFailures = writeFailures;
            FlushFailures = flushFailures;
            RotationCount = rotationCount;
            RotationFailures = rotationFailures;
            ArchiveCleanupFailures = archiveCleanupFailures;
            ArchiveEntriesInspected = archiveEntriesInspected;
            ArchiveFilesDeleted = archiveFilesDeleted;
            ArchiveCleanupPending = archiveCleanupPending;
            RecoveryCount = recoveryCount;
            RecoveryFailures = recoveryFailures;
            SuppressedDiagnostics = suppressedDiagnostics;
            CurrentFileBytes = currentFileBytes;
            Health = health;
            LastFailure = lastFailure;
            LastFailureUtc = lastFailureUtc;
        }
    }

    /// <summary>
    /// A bounded, synchronous file sink. Calls are serialized by an instance lock and do not retain
    /// the borrowed <see cref="LogEvent"/> after <see cref="Emit"/> returns.
    /// </summary>
    public sealed class FileLogSink : ILogSink, IFlushableLogSink, IIdempotentLogSinkDisposal, IMaintainableLogSink
    {
        private const int WRITE_BUFFER_CHARS = 4096;
        private const int FILE_STREAM_BUFFER_BYTES = 8192;
        private const int MAX_ARCHIVE_NAME_ATTEMPTS = 1024;
        internal const int ArchiveScanEntryBudget = 64;
        internal const int ArchiveDeletionBudget = 16;
        private const string ARCHIVE_MARKER = ".cyclone-v2-";

        private enum ArchiveCleanupResult : byte
        {
            Complete = 0,
            Pending = 1,
            Failed = 2
        }

        private struct ArchiveCandidate
        {
            public string Path;
            public string Name;
            public long TimestampTicks;
            public int CollisionSequence;
        }

        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly string TruncationSuffix = "... [TRUNCATED]" + Environment.NewLine;

        private readonly object _writeLock = new object();
        private readonly string _logFilePath;
        private readonly string _archivePrefix;
        private readonly string _archiveExtension;
        private readonly FileLogSinkOptions _options;
        private readonly char[] _buffer = new char[WRITE_BUFFER_CHARS];
        private readonly long _flushIntervalTicks;
        private readonly long _recoveryRetryTicks;
        private readonly ArchiveCandidate[] _oldestArchiveCandidates = new ArchiveCandidate[ArchiveDeletionBudget];

        public static bool IsSupported
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return false;
#else
                return true;
#endif
            }
        }
        private readonly long _diagnosticIntervalTicks;

        private FileStream _stream;
        private StreamWriter _writer;
        private volatile bool _disposed;
        private volatile FileLogSinkHealth _health;
        private bool _archivesNeedCleanup;
        private int _writesSinceFlush;
        private long _lastFlushTimestamp;
        private long _lastRecoveryAttemptTimestamp;
        private long _lastDiagnosticTimestamp;
        private bool _hasRecoveryAttemptTimestamp;
        private bool _hasDiagnosticTimestamp;
        private long _currentFileBytes;
        private IEnumerator<string> _archiveScanEnumerator;
        private long _archiveScanOwnedCount;
        private int _oldestArchiveCandidateCount;
        private long _archiveEntriesInspected;
        private bool _archiveRescanRequired;

        private long _attemptedEntries;
        private long _writtenEntries;
        private long _droppedEntries;
        private long _writeFailures;
        private long _flushFailures;
        private long _rotationCount;
        private long _rotationFailures;
        private long _archiveCleanupFailures;
        private long _archiveFilesDeleted;
        private long _recoveryCount;
        private long _recoveryFailures;
        private long _suppressedDiagnostics;
        private FileLogSinkFailureKind _lastFailure;
        private DateTime _lastFailureUtc;

        public string LogFilePath => _logFilePath;

        /// <summary>Returns the latest health state without waiting for file I/O.</summary>
        public FileLogSinkHealth Health => _health;

        internal long ArchiveEntriesInspected
        {
            get
            {
                lock (_writeLock)
                {
                    return _archiveEntriesInspected;
                }
            }
        }

        public FileLogSinkStatistics Statistics
        {
            get
            {
                lock (_writeLock)
                {
                    return new FileLogSinkStatistics(
                        _attemptedEntries,
                        _writtenEntries,
                        _droppedEntries,
                        _writeFailures,
                        _flushFailures,
                        _rotationCount,
                        _rotationFailures,
                        _archiveCleanupFailures,
                        _archiveEntriesInspected,
                        _archiveFilesDeleted,
                        _archivesNeedCleanup || _archiveScanEnumerator != null || _archiveRescanRequired,
                        _recoveryCount,
                        _recoveryFailures,
                        _suppressedDiagnostics,
                        _currentFileBytes,
                        _health,
                        _lastFailure,
                        _lastFailureUtc);
                }
            }
        }

        public FileLogSink(string logFilePath, FileLogSinkOptions options = null)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            _options = null;
            _logFilePath = null;
            _archivePrefix = null;
            _archiveExtension = null;
            _flushIntervalTicks = 0L;
            _recoveryRetryTicks = 0L;
            _diagnosticIntervalTicks = 0L;
            throw new PlatformNotSupportedException("FileLogSink is unavailable in WebGL players. Use a platform-provided remote or browser sink.");
#else
            _options = FileLogSinkOptions.CreateValidated(options);
            _logFilePath = GetCanonicalLogFilePath(logFilePath);

            string fileName = Path.GetFileName(_logFilePath);
            _archiveExtension = Path.GetExtension(fileName);
            _archivePrefix = Path.GetFileNameWithoutExtension(fileName) + ARCHIVE_MARKER;
            _flushIntervalTicks = MillisecondsToStopwatchTicks(_options.FlushIntervalMs);
            _recoveryRetryTicks = MillisecondsToStopwatchTicks(_options.RecoveryRetryIntervalMs);
            _diagnosticIntervalTicks = MillisecondsToStopwatchTicks(_options.DiagnosticIntervalMs);
            _archivesNeedCleanup = _options.MaintenanceMode == FileMaintenanceMode.Rotate;

            try
            {
                string directory = Path.GetDirectoryName(_logFilePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                Exception openFailure;
                if (!TryOpenWriterUnderLock(out openFailure))
                {
                    throw new IOException("The active log file could not be opened.", openFailure);
                }

                _health = FileLogSinkHealth.Healthy;
                _lastFlushTimestamp = Stopwatch.GetTimestamp();
                PerformMaintenanceUnderLock(forceRecovery: true);

                if (_writer == null)
                {
                    throw new IOException("The active log file could not be restored after maintenance.");
                }
            }
            catch (OutOfMemoryException)
            {
                _disposed = true;
                ResetArchiveCleanupUnderLock();
                _health = FileLogSinkHealth.Disposed;
                CloseWriterUnderLock(flush: false, out _);
                throw;
            }
            catch (Exception exception)
            {
                _disposed = true;
                ResetArchiveCleanupUnderLock();
                _health = FileLogSinkHealth.Disposed;
                CloseWriterUnderLock(flush: false, out _);
                TryWriteInitializationDiagnostic(exception);
                throw new InvalidOperationException("FileLogSink initialization failed.", exception);
            }
#endif
        }

        public void Emit(LogEvent logEvent)
        {
            if (logEvent == null)
            {
                throw new ArgumentNullException(nameof(logEvent));
            }

            if (_disposed)
            {
                return;
            }

            StringBuilder builder = StringBuilderPool.Get();
            bool attemptRecorded = false;
            try
            {
                FormatRecord(logEvent, builder);
                lock (_writeLock)
                {
                    _attemptedEntries++;
                    attemptRecorded = true;
                    if (_disposed)
                    {
                        _droppedEntries++;
                        return;
                    }

                    if (_writer == null && !TryRecoverWriterUnderLock(force: false))
                    {
                        _droppedEntries++;
                        return;
                    }

                    long recordBytes = GetUtf8ByteCount(builder, builder.Length);
                    if (!TryPrepareForWriteUnderLock(builder, ref recordBytes))
                    {
                        _droppedEntries++;
                        return;
                    }

                    try
                    {
                        WriteBuilderUnderLock(builder);
                        _currentFileBytes += recordBytes;
                        _writtenEntries++;
                        _writesSinceFlush++;
                    }
                    catch (Exception exception) when (!(exception is OutOfMemoryException))
                    {
                        _writeFailures++;
                        _droppedEntries++;
                        HandleWriterFailureUnderLock(FileLogSinkFailureKind.Write, exception);
                        return;
                    }

                    LogFlushMode flushMode = LogFlushMode.Buffered;
                    bool shouldFlush = logEvent.Severity >= LogSeverity.Error
                        || _writesSinceFlush >= _options.FlushBatchSize;

                    if (logEvent.Severity == LogSeverity.Fatal && _options.DurableFlushOnFatal)
                    {
                        flushMode = LogFlushMode.Durable;
                    }

                    if (!shouldFlush)
                    {
                        long now = Stopwatch.GetTimestamp();
                        shouldFlush = HasElapsed(now, _lastFlushTimestamp, _flushIntervalTicks);
                    }

                    if (shouldFlush)
                    {
                        TryFlushUnderLock(flushMode);
                    }
                }
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
                lock (_writeLock)
                {
                    if (!attemptRecorded)
                    {
                        _attemptedEntries++;
                    }
                    _droppedEntries++;
                    RecordFailureUnderLock(FileLogSinkFailureKind.Formatting, exception, writerUsable: _writer != null);
                }
            }
            finally
            {
                StringBuilderPool.Return(builder);
            }
        }

        public bool TryFlush(LogFlushMode mode)
        {
            if (mode != LogFlushMode.Buffered && mode != LogFlushMode.Durable)
            {
                throw new ArgumentOutOfRangeException(nameof(mode), "Unknown flush mode.");
            }

            lock (_writeLock)
            {
                if (_disposed)
                {
                    return false;
                }

                if (_writer == null && !TryRecoverWriterUnderLock(force: true))
                {
                    return false;
                }

                return TryFlushUnderLock(mode);
            }
        }

        /// <summary>
        /// Performs one bounded flush, recovery, rotation, and archive-cleanup maintenance step.
        /// Callers that use this sink without a <see cref="LogPipeline"/> must call this method
        /// periodically when incremental archive cleanup or idle flushing is required.
        /// </summary>
        public void PerformMaintenance()
        {
            lock (_writeLock)
            {
                if (_disposed)
                {
                    return;
                }

                PerformMaintenanceUnderLock(forceRecovery: false);
            }
        }

        void IMaintainableLogSink.PerformMaintenance()
        {
            PerformMaintenance();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            lock (_writeLock)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                ResetArchiveCleanupUnderLock();
                Exception closeFailure;
                if (!CloseWriterUnderLock(flush: true, out closeFailure) && closeFailure != null)
                {
                    RecordFailureUnderLock(FileLogSinkFailureKind.Dispose, closeFailure, writerUsable: false);
                }

                _health = FileLogSinkHealth.Disposed;
            }
        }

        private void FormatRecord(LogEvent logEvent, StringBuilder builder)
        {
            LogTimestampFormatter.FormatDateTimePrecise(logEvent.Timestamp, builder);
            builder.Append(" [");
            builder.Append(LogSeverityNames.Get(logEvent.Severity));
            builder.Append("] ");

            if (!string.IsNullOrEmpty(logEvent.Category))
            {
                builder.Append('[');
                AppendEscaped(builder, logEvent.Category, normalizePathSeparators: false, 0);
                builder.Append("] ");
            }

            logEvent.AppendMessageTo(builder, escapeControlCharacters: true);

            if (_options.SourcePathMode != LogSourcePathMode.None && !string.IsNullOrEmpty(logEvent.FilePath))
            {
                builder.Append(" (at ");
                int start = _options.SourcePathMode == LogSourcePathMode.FullPath
                    ? 0
                    : FindFileNameStart(logEvent.FilePath);
                AppendEscaped(builder, logEvent.FilePath, normalizePathSeparators: true, start);
                builder.Append(':');
                InvariantText.AppendInt32(builder, logEvent.LineNumber);
                builder.Append(')');
            }

            builder.AppendLine();
        }

        private bool TryPrepareForWriteUnderLock(StringBuilder builder, ref long recordBytes)
        {
            switch (_options.MaintenanceMode)
            {
                case FileMaintenanceMode.None:
                    return recordBytes > 0L;
                case FileMaintenanceMode.WarnOnly:
                    if (WouldExceedLimit(_currentFileBytes, recordBytes, _options.MaxFileBytes))
                    {
                        TryReportDiagnosticUnderLock(FileLogSinkFailureKind.None, null, "configured file size warning threshold exceeded");
                    }
                    return recordBytes > 0L;
                case FileMaintenanceMode.Rotate:
                    if (recordBytes > _options.MaxFileBytes)
                    {
                        TruncateRecordToByteLimit(builder, _options.MaxFileBytes);
                        recordBytes = GetUtf8ByteCount(builder, builder.Length);
                        if (recordBytes <= 0L)
                        {
                            return false;
                        }
                    }

                    if (_currentFileBytes > 0L && WouldExceedLimit(_currentFileBytes, recordBytes, _options.MaxFileBytes))
                    {
                        if (!TryRotateUnderLock())
                        {
                            return false;
                        }
                    }

                    return !WouldExceedLimit(_currentFileBytes, recordBytes, _options.MaxFileBytes);
                default:
                    return false;
            }
        }

        private void WriteBuilderUnderLock(StringBuilder builder)
        {
            int offset = 0;
            while (offset < builder.Length)
            {
                int count = Math.Min(_buffer.Length, builder.Length - offset);
                builder.CopyTo(offset, _buffer, 0, count);
                _writer.Write(_buffer, 0, count);
                offset += count;
            }
        }

        private bool TryFlushUnderLock(LogFlushMode mode)
        {
            try
            {
                _writer.Flush();
                _writesSinceFlush = 0;
                _lastFlushTimestamp = Stopwatch.GetTimestamp();
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
                _flushFailures++;
                HandleWriterFailureUnderLock(FileLogSinkFailureKind.Flush, exception);
                return false;
            }

            if (mode == LogFlushMode.Durable)
            {
                try
                {
                    _stream.Flush(flushToDisk: true);
                }
                catch (Exception exception) when (!(exception is OutOfMemoryException))
                {
                    _flushFailures++;
                    RecordFailureUnderLock(FileLogSinkFailureKind.DurableFlush, exception, writerUsable: _writer != null);
                    return false;
                }
            }

            return true;
        }

        private void PerformMaintenanceUnderLock(bool forceRecovery)
        {
            if (_writer == null && !TryRecoverWriterUnderLock(forceRecovery))
            {
                return;
            }

            if (_writer != null
                && !File.Exists(_logFilePath)
                && !TryRestoreMissingActivePathUnderLock())
            {
                return;
            }

            if (_writesSinceFlush > 0
                && HasElapsed(Stopwatch.GetTimestamp(), _lastFlushTimestamp, _flushIntervalTicks)
                && !TryFlushUnderLock(LogFlushMode.Buffered))
            {
                return;
            }

            switch (_options.MaintenanceMode)
            {
                case FileMaintenanceMode.None:
                    return;
                case FileMaintenanceMode.WarnOnly:
                    if (_currentFileBytes > _options.MaxFileBytes)
                    {
                        TryReportDiagnosticUnderLock(FileLogSinkFailureKind.None, null, "configured file size warning threshold exceeded");
                    }
                    return;
                case FileMaintenanceMode.Rotate:
                    if (_currentFileBytes > _options.MaxFileBytes)
                    {
                        TryRotateUnderLock();
                        return;
                    }

                    if (_archivesNeedCleanup)
                    {
                        ArchiveCleanupResult cleanupResult = TryCleanupArchivesUnderLock();
                        _archivesNeedCleanup = cleanupResult != ArchiveCleanupResult.Complete;
                    }
                    return;
            }
        }

        private bool TryRestoreMissingActivePathUnderLock()
        {
            Exception reachabilityFailure = new IOException(
                "The active log file path disappeared while its writer was open.");
            Exception closeFailure;
            if (!CloseWriterUnderLock(flush: true, out closeFailure) && closeFailure != null)
            {
                reachabilityFailure = new IOException(
                    "The active log file path disappeared and its detached writer did not close cleanly.",
                    closeFailure);
            }

            RecordFailureUnderLock(
                FileLogSinkFailureKind.Recovery,
                reachabilityFailure,
                writerUsable: false);
            return TryRecoverWriterUnderLock(force: true);
        }

        private bool TryRotateUnderLock()
        {
            if (_writer == null && !TryRecoverWriterUnderLock(force: true))
            {
                return false;
            }

            Exception closeFailure;
            if (!CloseWriterUnderLock(flush: true, out closeFailure))
            {
                _rotationFailures++;
                RecordFailureUnderLock(FileLogSinkFailureKind.Rotation, closeFailure, writerUsable: false);
                TryRecoverWriterUnderLock(force: true);
                return false;
            }

            if (!File.Exists(_logFilePath))
            {
                Exception missingFileRecoveryFailure;
                if (!TryOpenWriterUnderLock(out missingFileRecoveryFailure))
                {
                    _rotationFailures++;
                    RecordFailureUnderLock(FileLogSinkFailureKind.Rotation, missingFileRecoveryFailure, writerUsable: false);
                    return false;
                }

                _rotationCount++;
                _health = FileLogSinkHealth.Degraded;
                return true;
            }

            string archivePath;
            try
            {
                archivePath = GetAvailableArchivePath();
                File.Move(_logFilePath, archivePath);
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
                _rotationFailures++;
                RecordFailureUnderLock(FileLogSinkFailureKind.Rotation, exception, writerUsable: false);
                TryRecoverWriterUnderLock(force: true);
                return false;
            }

            Exception openFailure;
            if (!TryOpenWriterUnderLock(out openFailure))
            {
                _rotationFailures++;
                RecordFailureUnderLock(FileLogSinkFailureKind.Rotation, openFailure, writerUsable: false);

                Exception retryFailure;
                if (!TryOpenWriterUnderLock(out retryFailure))
                {
                    if (!TryRollbackArchiveUnderLock(archivePath))
                    {
                        RecordFailureUnderLock(FileLogSinkFailureKind.Recovery, retryFailure, writerUsable: false);
                    }

                    // The old active file was restored or recovery failed. In either case the
                    // triggering record must not be appended beyond the configured size limit.
                    return false;
                }

                _health = FileLogSinkHealth.Degraded;
            }

            _rotationCount++;
            _archivesNeedCleanup = true;
            _archiveRescanRequired = true;
            ArchiveCleanupResult cleanupResult = TryCleanupArchivesUnderLock();
            if (cleanupResult == ArchiveCleanupResult.Failed)
            {
                _archivesNeedCleanup = true;
                _health = FileLogSinkHealth.Degraded;
            }
            else
            {
                _archivesNeedCleanup = cleanupResult != ArchiveCleanupResult.Complete;
                if (!_archivesNeedCleanup && _health != FileLogSinkHealth.Degraded)
                {
                    _health = FileLogSinkHealth.Healthy;
                }
            }

            return _writer != null;
        }

        private bool TryRollbackArchiveUnderLock(string archivePath)
        {
            try
            {
                if (File.Exists(_logFilePath))
                {
                    var current = new FileInfo(_logFilePath);
                    if (current.Length != 0L)
                    {
                        Exception existingFileOpenFailure;
                        return TryOpenWriterUnderLock(out existingFileOpenFailure);
                    }

                    File.Delete(_logFilePath);
                }

                if (File.Exists(archivePath))
                {
                    File.Move(archivePath, _logFilePath);
                }

                Exception rollbackOpenFailure;
                if (TryOpenWriterUnderLock(out rollbackOpenFailure))
                {
                    _health = FileLogSinkHealth.Degraded;
                    return true;
                }

                RecordFailureUnderLock(FileLogSinkFailureKind.Recovery, rollbackOpenFailure, writerUsable: false);
                return false;
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
                RecordFailureUnderLock(FileLogSinkFailureKind.Recovery, exception, writerUsable: false);
                return false;
            }
        }

        private string GetAvailableArchivePath()
        {
            string directory = Path.GetDirectoryName(_logFilePath);
            if (string.IsNullOrEmpty(directory))
            {
                throw new IOException("The active log file has no parent directory.");
            }

            string timestamp = DateTime.UtcNow.Ticks.ToString("D19", CultureInfo.InvariantCulture);
            for (int attempt = 0; attempt < MAX_ARCHIVE_NAME_ATTEMPTS; attempt++)
            {
                string sequence = attempt == 0
                    ? string.Empty
                    : "-" + attempt.ToString(CultureInfo.InvariantCulture);
                string archivePath = Path.Combine(directory, _archivePrefix + timestamp + sequence + _archiveExtension);
                if (!File.Exists(archivePath))
                {
                    return archivePath;
                }
            }

            throw new IOException("No unique archive name was available.");
        }

        private ArchiveCleanupResult TryCleanupArchivesUnderLock()
        {
            try
            {
                string directory = Path.GetDirectoryName(_logFilePath);
                if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                {
                    ResetArchiveCleanupUnderLock();
                    return ArchiveCleanupResult.Complete;
                }

                if (_archiveScanEnumerator == null)
                {
                    _archiveScanEnumerator = Directory
                        .EnumerateFileSystemEntries(
                            directory,
                            "*",
                            SearchOption.TopDirectoryOnly)
                        .GetEnumerator();
                    _archiveRescanRequired = false;
                }

                int inspectedThisCall = 0;
                while (inspectedThisCall < ArchiveScanEntryBudget)
                {
                    if (!_archiveScanEnumerator.MoveNext())
                    {
                        return CompleteArchiveScanUnderLock();
                    }

                    inspectedThisCall++;
                    _archiveEntriesInspected++;

                    string path = _archiveScanEnumerator.Current;
                    string fileName = Path.GetFileName(path);
                    long timestampTicks;
                    int collisionSequence;
                    if (File.Exists(path)
                        && TryGetOwnedArchiveOrder(fileName, out timestampTicks, out collisionSequence))
                    {
                        _archiveScanOwnedCount++;
                        ConsiderOldestArchiveCandidate(
                            path,
                            fileName,
                            timestampTicks,
                            collisionSequence);
                    }
                }

                return ArchiveCleanupResult.Pending;
            }
            catch (DirectoryNotFoundException)
            {
                ResetArchiveCleanupUnderLock();
                return ArchiveCleanupResult.Complete;
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
                ResetArchiveScanPassUnderLock();
                _archiveCleanupFailures++;
                RecordFailureUnderLock(FileLogSinkFailureKind.ArchiveCleanup, exception, writerUsable: _writer != null);
                return ArchiveCleanupResult.Failed;
            }
        }

        private ArchiveCleanupResult CompleteArchiveScanUnderLock()
        {
            DisposeArchiveScanEnumeratorUnderLock();
            long excessArchiveCount = _archiveScanOwnedCount - _options.MaxArchiveFiles;
            if (excessArchiveCount <= 0)
            {
                bool rescanRequired = _archiveRescanRequired;
                ResetArchiveScanPassUnderLock();
                return rescanRequired
                    ? ArchiveCleanupResult.Pending
                    : ArchiveCleanupResult.Complete;
            }

            int deleteCount = (int)Math.Min(excessArchiveCount, _oldestArchiveCandidateCount);
            bool allDeleted = true;
            for (int i = 0; i < deleteCount; i++)
            {
                int oldestIndex = FindOldestArchiveCandidateIndex();
                string path = _oldestArchiveCandidates[oldestIndex].Path;
                RemoveArchiveCandidateAt(oldestIndex);

                try
                {
                    File.Delete(path);
                    _archiveFilesDeleted++;
                }
                catch (Exception exception) when (!(exception is OutOfMemoryException))
                {
                    allDeleted = false;
                    _archiveCleanupFailures++;
                    RecordFailureUnderLock(FileLogSinkFailureKind.ArchiveCleanup, exception, writerUsable: _writer != null);
                }
            }

            _archiveRescanRequired = true;
            ResetArchiveScanPassUnderLock();
            if (!allDeleted)
            {
                return ArchiveCleanupResult.Failed;
            }

            return ArchiveCleanupResult.Pending;
        }

        private void ConsiderOldestArchiveCandidate(
            string path,
            string fileName,
            long timestampTicks,
            int collisionSequence)
        {
            var candidate = new ArchiveCandidate
            {
                Path = path,
                Name = fileName,
                TimestampTicks = timestampTicks,
                CollisionSequence = collisionSequence
            };

            if (_oldestArchiveCandidateCount < _oldestArchiveCandidates.Length)
            {
                _oldestArchiveCandidates[_oldestArchiveCandidateCount++] = candidate;
                return;
            }

            int newestIndex = FindNewestArchiveCandidateIndex();
            if (CompareArchiveAge(candidate, _oldestArchiveCandidates[newestIndex]) < 0)
            {
                _oldestArchiveCandidates[newestIndex] = candidate;
            }
        }

        private int FindOldestArchiveCandidateIndex()
        {
            int oldestIndex = 0;
            for (int i = 1; i < _oldestArchiveCandidateCount; i++)
            {
                if (CompareArchiveAge(_oldestArchiveCandidates[i], _oldestArchiveCandidates[oldestIndex]) < 0)
                {
                    oldestIndex = i;
                }
            }

            return oldestIndex;
        }

        private int FindNewestArchiveCandidateIndex()
        {
            int newestIndex = 0;
            for (int i = 1; i < _oldestArchiveCandidateCount; i++)
            {
                if (CompareArchiveAge(_oldestArchiveCandidates[i], _oldestArchiveCandidates[newestIndex]) > 0)
                {
                    newestIndex = i;
                }
            }

            return newestIndex;
        }

        private void RemoveArchiveCandidateAt(int index)
        {
            int lastIndex = --_oldestArchiveCandidateCount;
            _oldestArchiveCandidates[index] = _oldestArchiveCandidates[lastIndex];
            _oldestArchiveCandidates[lastIndex] = default;
        }

        private void ResetArchiveScanPassUnderLock()
        {
            DisposeArchiveScanEnumeratorUnderLock();

            for (int i = 0; i < _oldestArchiveCandidateCount; i++)
            {
                _oldestArchiveCandidates[i] = default;
            }

            _oldestArchiveCandidateCount = 0;
            _archiveScanOwnedCount = 0L;
        }

        private void ResetArchiveCleanupUnderLock()
        {
            ResetArchiveScanPassUnderLock();
            _archiveRescanRequired = false;
        }

        private void DisposeArchiveScanEnumeratorUnderLock()
        {
            if (_archiveScanEnumerator == null)
            {
                return;
            }

            try
            {
                _archiveScanEnumerator.Dispose();
            }
            catch
            {
            }

            _archiveScanEnumerator = null;
        }

        private bool TryGetOwnedArchiveOrder(
            string fileName,
            out long timestampTicks,
            out int collisionSequence)
        {
            timestampTicks = 0L;
            collisionSequence = 0;
            if (!fileName.StartsWith(_archivePrefix, StringComparison.Ordinal)
                || !_archiveExtension.Equals(Path.GetExtension(fileName), StringComparison.Ordinal))
            {
                return false;
            }

            int extensionLength = _archiveExtension.Length;
            int tokenLength = fileName.Length - _archivePrefix.Length - extensionLength;
            if (tokenLength <= 0)
            {
                return false;
            }

            string token = fileName.Substring(_archivePrefix.Length, tokenLength);
            if (TryParseArchiveToken(token, out timestampTicks))
            {
                return true;
            }

            int separator = token.LastIndexOf('-');
            if (separator <= 0 || separator == token.Length - 1)
            {
                return false;
            }

            if (!int.TryParse(
                    token.Substring(separator + 1),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out collisionSequence)
                || collisionSequence < 1)
            {
                return false;
            }

            return TryParseArchiveToken(token.Substring(0, separator), out timestampTicks);
        }

        private static bool TryParseArchiveToken(string token, out long timestampTicks)
        {
            timestampTicks = 0L;
            if (token == null || token.Length != 19)
            {
                return false;
            }

            return long.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out timestampTicks)
                && timestampTicks >= DateTime.MinValue.Ticks
                && timestampTicks <= DateTime.MaxValue.Ticks;
        }

        private bool TryRecoverWriterUnderLock(bool force)
        {
            if (_writer != null)
            {
                return true;
            }

            long now = Stopwatch.GetTimestamp();
            if (!force
                && _hasRecoveryAttemptTimestamp
                && !HasElapsed(now, _lastRecoveryAttemptTimestamp, _recoveryRetryTicks))
            {
                return false;
            }

            _hasRecoveryAttemptTimestamp = true;
            _lastRecoveryAttemptTimestamp = now;
            Exception recoveryFailure;
            if (!TryOpenWriterUnderLock(out recoveryFailure))
            {
                _recoveryFailures++;
                RecordFailureUnderLock(FileLogSinkFailureKind.Recovery, recoveryFailure, writerUsable: false);
                return false;
            }

            _recoveryCount++;
            _health = FileLogSinkHealth.Degraded;
            _lastFlushTimestamp = now;
            return true;
        }

        private bool TryOpenWriterUnderLock(out Exception failure)
        {
            FileStream stream = null;
            StreamWriter writer = null;
            try
            {
                string directory = Path.GetDirectoryName(_logFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    ResetArchiveCleanupUnderLock();
                    Directory.CreateDirectory(directory);
                    _archivesNeedCleanup = _options.MaintenanceMode == FileMaintenanceMode.Rotate;
                }

                stream = new FileStream(
                    _logFilePath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.Read,
                    FILE_STREAM_BUFFER_BYTES,
                    FileOptions.SequentialScan);
                writer = new StreamWriter(stream, Utf8NoBom, WRITE_BUFFER_CHARS)
                {
                    AutoFlush = false
                };

                _stream = stream;
                _writer = writer;
                _currentFileBytes = stream.Length;
                _writesSinceFlush = 0;
                failure = null;
                return true;
            }
            catch (OutOfMemoryException)
            {
                DisposeFailedOpenResources(writer, stream);
                _stream = null;
                _writer = null;
                _currentFileBytes = 0L;
                _health = FileLogSinkHealth.Faulted;
                throw;
            }
            catch (Exception exception)
            {
                DisposeFailedOpenResources(writer, stream);

                _stream = null;
                _writer = null;
                _currentFileBytes = 0L;
                _health = FileLogSinkHealth.Faulted;
                failure = exception;
                return false;
            }
        }

        private bool CloseWriterUnderLock(bool flush, out Exception failure)
        {
            StreamWriter writer = _writer;
            _writer = null;
            _stream = null;
            failure = null;
            OutOfMemoryException fatalFailure = null;

            if (writer == null)
            {
                return true;
            }

            if (flush)
            {
                try
                {
                    writer.Flush();
                }
                catch (OutOfMemoryException exception)
                {
                    fatalFailure = exception;
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            }

            try
            {
                writer.Dispose();
            }
            catch (OutOfMemoryException exception)
            {
                if (fatalFailure == null)
                {
                    fatalFailure = exception;
                }
            }
            catch (Exception exception)
            {
                if (failure == null)
                {
                    failure = exception;
                }
            }

            if (fatalFailure != null)
            {
                throw fatalFailure;
            }

            return failure == null;
        }

        private static void DisposeFailedOpenResources(StreamWriter writer, FileStream stream)
        {
            try
            {
                writer?.Dispose();
                if (writer == null)
                {
                    stream?.Dispose();
                }
            }
            catch
            {
                // Preserve the original open failure; resource cleanup is best effort.
            }
        }

        private void HandleWriterFailureUnderLock(FileLogSinkFailureKind kind, Exception exception)
        {
            CloseWriterUnderLock(flush: false, out _);
            RecordFailureUnderLock(kind, exception, writerUsable: false);
            TryRecoverWriterUnderLock(force: true);
        }

        private void RecordFailureUnderLock(FileLogSinkFailureKind kind, Exception exception, bool writerUsable)
        {
            _lastFailure = kind;
            _lastFailureUtc = DateTime.UtcNow;
            _health = writerUsable ? FileLogSinkHealth.Degraded : FileLogSinkHealth.Faulted;
            TryReportDiagnosticUnderLock(kind, exception, null);
        }

        private void TryReportDiagnosticUnderLock(FileLogSinkFailureKind kind, Exception exception, string detail)
        {
            long now = Stopwatch.GetTimestamp();
            if (_hasDiagnosticTimestamp && !HasElapsed(now, _lastDiagnosticTimestamp, _diagnosticIntervalTicks))
            {
                _suppressedDiagnostics++;
                return;
            }

            _hasDiagnosticTimestamp = true;
            _lastDiagnosticTimestamp = now;
            try
            {
                string description = detail ?? "sink operation failed";
                string severity = kind == FileLogSinkFailureKind.None ? "WARNING" : "ERROR";
                EmergencyLogWriter.TryWrite(
                    "[" + severity + "] FileLogSink: " + description
                    + "; kind=" + kind
                    + ".",
                    exception);
            }
            catch
            {
            }
        }

        private long GetUtf8ByteCount(StringBuilder builder, int length)
        {
            long byteCount = 0L;
            int offset = 0;
            while (offset < length)
            {
                int count = Math.Min(_buffer.Length, length - offset);
                if (count > 1
                    && offset + count < length
                    && char.IsHighSurrogate(builder[offset + count - 1]))
                {
                    count--;
                }

                builder.CopyTo(offset, _buffer, 0, count);
                byteCount += Utf8NoBom.GetByteCount(_buffer, 0, count);
                offset += count;
            }

            return byteCount;
        }

        private void TruncateRecordToByteLimit(StringBuilder builder, long maxBytes)
        {
            long suffixBytes = Utf8NoBom.GetByteCount(TruncationSuffix);
            if (suffixBytes <= maxBytes)
            {
                int contentLength = Math.Max(0, builder.Length - Environment.NewLine.Length);
                int prefixLength = FindLargestPrefixWithinByteLimit(builder, contentLength, maxBytes - suffixBytes);
                if (prefixLength > 0 && char.IsHighSurrogate(builder[prefixLength - 1]))
                {
                    prefixLength--;
                }

                builder.Length = prefixLength;
                builder.Append(TruncationSuffix);
                return;
            }

            int maximumLength = FindLargestPrefixWithinByteLimit(builder, builder.Length, maxBytes);
            if (maximumLength > 0 && char.IsHighSurrogate(builder[maximumLength - 1]))
            {
                maximumLength--;
            }

            builder.Length = maximumLength;
        }

        private int FindLargestPrefixWithinByteLimit(StringBuilder builder, int maximumLength, long maxBytes)
        {
            int low = 0;
            int high = maximumLength;
            while (low < high)
            {
                int midpoint = low + ((high - low + 1) / 2);
                long byteCount = GetUtf8ByteCount(builder, midpoint);
                if (byteCount <= maxBytes)
                {
                    low = midpoint;
                }
                else
                {
                    high = midpoint - 1;
                }
            }

            return low;
        }

        private static string GetCanonicalLogFilePath(string logFilePath)
        {
            if (string.IsNullOrWhiteSpace(logFilePath))
            {
                throw new ArgumentException("A log file path is required.", nameof(logFilePath));
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(logFilePath);
            }
            catch (Exception exception) when (exception is ArgumentException
                || exception is NotSupportedException
                || exception is PathTooLongException)
            {
                throw new ArgumentException("The log file path is invalid.", nameof(logFilePath), exception);
            }

            string fileName = Path.GetFileName(fullPath);
            if (string.IsNullOrEmpty(fileName)
                || fileName == "."
                || fileName == ".."
                || fileName[fileName.Length - 1] == '.'
                || fileName[fileName.Length - 1] == ' ')
            {
                throw new ArgumentException("The log file path must end with a portable file name.", nameof(logFilePath));
            }

            for (int i = 0; i < fileName.Length; i++)
            {
                if (IsInvalidPortableFileNameCharacter(fileName[i]))
                {
                    throw new ArgumentException("The log file name contains a non-portable character.", nameof(logFilePath));
                }
            }

            string deviceName = Path.GetFileNameWithoutExtension(fileName);
            if (IsReservedWindowsDeviceName(deviceName))
            {
                throw new ArgumentException("The log file name is reserved on Windows.", nameof(logFilePath));
            }

            return fullPath;
        }

        private static bool IsInvalidPortableFileNameCharacter(char character)
        {
            if (character < 32 || character == 127)
            {
                return true;
            }

            switch (character)
            {
                case '<':
                case '>':
                case ':':
                case '"':
                case '/':
                case '\\':
                case '|':
                case '?':
                case '*':
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsReservedWindowsDeviceName(string fileNameWithoutExtension)
        {
            if (fileNameWithoutExtension.Equals("CON", StringComparison.OrdinalIgnoreCase)
                || fileNameWithoutExtension.Equals("PRN", StringComparison.OrdinalIgnoreCase)
                || fileNameWithoutExtension.Equals("AUX", StringComparison.OrdinalIgnoreCase)
                || fileNameWithoutExtension.Equals("NUL", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (fileNameWithoutExtension.Length != 4)
            {
                return false;
            }

            char suffix = fileNameWithoutExtension[3];
            if (suffix < '1' || suffix > '9')
            {
                return false;
            }

            return fileNameWithoutExtension.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                || fileNameWithoutExtension.StartsWith("LPT", StringComparison.OrdinalIgnoreCase);
        }

        private static int FindFileNameStart(string path)
        {
            int start = 0;
            for (int i = 0; i < path.Length; i++)
            {
                if (path[i] == '/' || path[i] == '\\')
                {
                    start = i + 1;
                }
            }

            return start;
        }

        private static void AppendEscaped(StringBuilder destination, string source, bool normalizePathSeparators, int start)
        {
            for (int i = start; i < source.Length; i++)
            {
                char character = source[i];
                if (normalizePathSeparators && character == '\\')
                {
                    destination.Append('/');
                }
                else if (!char.IsControl(character))
                {
                    destination.Append(character);
                }
                else
                {
                    AppendEscapedControlCharacter(destination, character);
                }
            }
        }

        private static void AppendEscapedControlCharacter(StringBuilder destination, char character)
        {
            switch (character)
            {
                case '\r':
                    destination.Append("\\r");
                    return;
                case '\n':
                    destination.Append("\\n");
                    return;
                case '\t':
                    destination.Append("\\t");
                    return;
                default:
                    const string HEX = "0123456789ABCDEF";
                    destination.Append("\\u");
                    destination.Append(HEX[(character >> 12) & 0xF]);
                    destination.Append(HEX[(character >> 8) & 0xF]);
                    destination.Append(HEX[(character >> 4) & 0xF]);
                    destination.Append(HEX[character & 0xF]);
                    return;
            }
        }

        private static int CompareArchiveAge(ArchiveCandidate left, ArchiveCandidate right)
        {
            int timestampComparison = left.TimestampTicks.CompareTo(right.TimestampTicks);
            if (timestampComparison != 0)
            {
                return timestampComparison;
            }

            int sequenceComparison = left.CollisionSequence.CompareTo(right.CollisionSequence);
            return sequenceComparison != 0
                ? sequenceComparison
                : string.CompareOrdinal(left.Name, right.Name);
        }

        private static bool WouldExceedLimit(long currentBytes, long additionalBytes, long limit)
        {
            return currentBytes > limit || additionalBytes > limit - currentBytes;
        }

        private static long MillisecondsToStopwatchTicks(int milliseconds)
        {
            if (milliseconds <= 0)
            {
                return 0L;
            }

            double ticks = milliseconds * (Stopwatch.Frequency / 1000.0);
            return ticks >= long.MaxValue ? long.MaxValue : (long)ticks;
        }

        private static bool HasElapsed(long now, long then, long intervalTicks)
        {
            return intervalTicks <= 0L || now - then >= intervalTicks;
        }

        private static void TryWriteInitializationDiagnostic(Exception exception)
        {
            EmergencyLogWriter.TryWrite(
                "[ERROR] FileLogSink: initialization failed.",
                exception);
        }
    }
}
