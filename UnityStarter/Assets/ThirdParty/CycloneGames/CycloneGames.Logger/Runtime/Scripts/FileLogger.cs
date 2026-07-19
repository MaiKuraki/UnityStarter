using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using CycloneGames.Logger.Util;

namespace CycloneGames.Logger
{
    public enum FileLoggerHealth : byte
    {
        Healthy = 0,
        Degraded = 1,
        Faulted = 2,
        Disposed = 3
    }

    public enum FileLoggerFailureKind : byte
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
    /// Immutable health and throughput snapshot for a <see cref="FileLogger"/> instance.
    /// Counts are lifetime totals and are safe to read from any thread.
    /// </summary>
    public readonly struct FileLoggerStatistics
    {
        public long AttemptedEntries { get; }
        public long WrittenEntries { get; }
        public long DroppedEntries { get; }
        public long WriteFailures { get; }
        public long FlushFailures { get; }
        public long RotationCount { get; }
        public long RotationFailures { get; }
        public long ArchiveCleanupFailures { get; }
        public long RecoveryCount { get; }
        public long RecoveryFailures { get; }
        public long SuppressedDiagnostics { get; }
        public long CurrentFileBytes { get; }
        public FileLoggerHealth Health { get; }
        public FileLoggerFailureKind LastFailure { get; }
        public DateTime LastFailureUtc { get; }

        internal FileLoggerStatistics(
            long attemptedEntries,
            long writtenEntries,
            long droppedEntries,
            long writeFailures,
            long flushFailures,
            long rotationCount,
            long rotationFailures,
            long archiveCleanupFailures,
            long recoveryCount,
            long recoveryFailures,
            long suppressedDiagnostics,
            long currentFileBytes,
            FileLoggerHealth health,
            FileLoggerFailureKind lastFailure,
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
    /// the borrowed <see cref="LogMessage"/> after <see cref="Log"/> returns.
    /// </summary>
    public sealed class FileLogger : ILogger, IFlushableLogger, IIdempotentLoggerSinkDisposal, IMaintainableLogger
    {
        private const int WRITE_BUFFER_CHARS = 4096;
        private const int FILE_STREAM_BUFFER_BYTES = 8192;
        private const int MAX_ARCHIVE_NAME_ATTEMPTS = 1024;
        private const string ARCHIVE_MARKER = ".cyclone-v2-";

        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly string TruncationSuffix = "... [TRUNCATED]" + Environment.NewLine;

        private readonly object _writeLock = new object();
        private readonly string _logFilePath;
        private readonly string _archivePrefix;
        private readonly string _archiveExtension;
        private readonly FileLoggerOptions _options;
        private readonly char[] _buffer = new char[WRITE_BUFFER_CHARS];
        private readonly long _flushIntervalTicks;
        private readonly long _recoveryRetryTicks;

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
        private volatile FileLoggerHealth _health;
        private bool _archivesNeedCleanup;
        private int _writesSinceFlush;
        private long _lastFlushTimestamp;
        private long _lastRecoveryAttemptTimestamp;
        private long _lastDiagnosticTimestamp;
        private bool _hasRecoveryAttemptTimestamp;
        private bool _hasDiagnosticTimestamp;
        private long _currentFileBytes;

        private long _attemptedEntries;
        private long _writtenEntries;
        private long _droppedEntries;
        private long _writeFailures;
        private long _flushFailures;
        private long _rotationCount;
        private long _rotationFailures;
        private long _archiveCleanupFailures;
        private long _recoveryCount;
        private long _recoveryFailures;
        private long _suppressedDiagnostics;
        private FileLoggerFailureKind _lastFailure;
        private DateTime _lastFailureUtc;

        public string LogFilePath => _logFilePath;

        /// <summary>Returns the latest health state without waiting for file I/O.</summary>
        public FileLoggerHealth Health => _health;

        public FileLoggerStatistics Statistics
        {
            get
            {
                lock (_writeLock)
                {
                    return new FileLoggerStatistics(
                        _attemptedEntries,
                        _writtenEntries,
                        _droppedEntries,
                        _writeFailures,
                        _flushFailures,
                        _rotationCount,
                        _rotationFailures,
                        _archiveCleanupFailures,
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

        public FileLogger(string logFilePath, FileLoggerOptions options = null)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            _options = null;
            _logFilePath = null;
            _archivePrefix = null;
            _archiveExtension = null;
            _flushIntervalTicks = 0L;
            _recoveryRetryTicks = 0L;
            _diagnosticIntervalTicks = 0L;
            throw new PlatformNotSupportedException("FileLogger is unavailable in WebGL players. Use a platform-provided remote or browser sink.");
#else
            _options = FileLoggerOptions.CreateValidated(options);
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

                _health = FileLoggerHealth.Healthy;
                _lastFlushTimestamp = Stopwatch.GetTimestamp();
                PerformMaintenanceUnderLock(forceRecovery: true);

                if (_writer == null)
                {
                    throw new IOException("The active log file could not be restored after maintenance.");
                }
            }
            catch (Exception exception)
            {
                _disposed = true;
                _health = FileLoggerHealth.Disposed;
                CloseWriterUnderLock(flush: false, out _);
                TryWriteInitializationDiagnostic(exception);
                throw new InvalidOperationException("FileLogger initialization failed.", exception);
            }
#endif
        }

        public void Log(LogMessage logMessage)
        {
            if (logMessage == null)
            {
                throw new ArgumentNullException(nameof(logMessage));
            }

            if (_disposed)
            {
                return;
            }

            StringBuilder builder = StringBuilderPool.Get();
            bool attemptRecorded = false;
            try
            {
                FormatRecord(logMessage, builder);
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
                    catch (Exception exception)
                    {
                        _writeFailures++;
                        _droppedEntries++;
                        HandleWriterFailureUnderLock(FileLoggerFailureKind.Write, exception);
                        return;
                    }

                    LogFlushMode flushMode = LogFlushMode.Buffered;
                    bool shouldFlush = logMessage.Level >= LogLevel.Error
                        || _writesSinceFlush >= _options.FlushBatchSize;

                    if (logMessage.Level == LogLevel.Fatal && _options.DurableFlushOnFatal)
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
            catch (Exception exception)
            {
                lock (_writeLock)
                {
                    if (!attemptRecorded)
                    {
                        _attemptedEntries++;
                    }
                    _droppedEntries++;
                    RecordFailureUnderLock(FileLoggerFailureKind.Formatting, exception, writerUsable: _writer != null);
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

        void IMaintainableLogger.PerformMaintenance()
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
                Exception closeFailure;
                if (!CloseWriterUnderLock(flush: true, out closeFailure) && closeFailure != null)
                {
                    RecordFailureUnderLock(FileLoggerFailureKind.Dispose, closeFailure, writerUsable: false);
                }

                _health = FileLoggerHealth.Disposed;
            }
        }

        private void FormatRecord(LogMessage logMessage, StringBuilder builder)
        {
            DateTimeUtil.FormatDateTimePrecise(logMessage.Timestamp, builder);
            builder.Append(" [");
            builder.Append(LogLevelStrings.Get(logMessage.Level));
            builder.Append("] ");

            if (!string.IsNullOrEmpty(logMessage.Category))
            {
                builder.Append('[');
                AppendEscaped(builder, logMessage.Category, normalizePathSeparators: false, 0);
                builder.Append("] ");
            }

            logMessage.AppendMessageTo(builder, escapeControlCharacters: true);

            if (_options.SourcePathMode != LogSourcePathMode.None && !string.IsNullOrEmpty(logMessage.FilePath))
            {
                builder.Append(" (at ");
                int start = _options.SourcePathMode == LogSourcePathMode.FullPath
                    ? 0
                    : FindFileNameStart(logMessage.FilePath);
                AppendEscaped(builder, logMessage.FilePath, normalizePathSeparators: true, start);
                builder.Append(':');
                InvariantText.AppendInt32(builder, logMessage.LineNumber);
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
                        TryReportDiagnosticUnderLock(FileLoggerFailureKind.None, null, "configured file size warning threshold exceeded");
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
            catch (Exception exception)
            {
                _flushFailures++;
                HandleWriterFailureUnderLock(FileLoggerFailureKind.Flush, exception);
                return false;
            }

            if (mode == LogFlushMode.Durable)
            {
                try
                {
                    _stream.Flush(flushToDisk: true);
                }
                catch (Exception exception)
                {
                    _flushFailures++;
                    RecordFailureUnderLock(FileLoggerFailureKind.DurableFlush, exception, writerUsable: _writer != null);
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
                        TryReportDiagnosticUnderLock(FileLoggerFailureKind.None, null, "configured file size warning threshold exceeded");
                    }
                    return;
                case FileMaintenanceMode.Rotate:
                    if (_currentFileBytes > _options.MaxFileBytes && !TryRotateUnderLock())
                    {
                        return;
                    }

                    if (_archivesNeedCleanup)
                    {
                        _archivesNeedCleanup = !TryCleanupArchivesUnderLock();
                    }
                    return;
            }
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
                RecordFailureUnderLock(FileLoggerFailureKind.Rotation, closeFailure, writerUsable: false);
                TryRecoverWriterUnderLock(force: true);
                return false;
            }

            if (!File.Exists(_logFilePath))
            {
                Exception missingFileRecoveryFailure;
                if (!TryOpenWriterUnderLock(out missingFileRecoveryFailure))
                {
                    _rotationFailures++;
                    RecordFailureUnderLock(FileLoggerFailureKind.Rotation, missingFileRecoveryFailure, writerUsable: false);
                    return false;
                }

                _rotationCount++;
                _health = FileLoggerHealth.Degraded;
                return true;
            }

            string archivePath;
            try
            {
                archivePath = GetAvailableArchivePath();
                File.Move(_logFilePath, archivePath);
            }
            catch (Exception exception)
            {
                _rotationFailures++;
                RecordFailureUnderLock(FileLoggerFailureKind.Rotation, exception, writerUsable: false);
                TryRecoverWriterUnderLock(force: true);
                return false;
            }

            Exception openFailure;
            if (!TryOpenWriterUnderLock(out openFailure))
            {
                _rotationFailures++;
                RecordFailureUnderLock(FileLoggerFailureKind.Rotation, openFailure, writerUsable: false);

                Exception retryFailure;
                if (!TryOpenWriterUnderLock(out retryFailure))
                {
                    if (!TryRollbackArchiveUnderLock(archivePath))
                    {
                        RecordFailureUnderLock(FileLoggerFailureKind.Recovery, retryFailure, writerUsable: false);
                    }

                    // The old active file was restored or recovery failed. In either case the
                    // triggering record must not be appended beyond the configured size limit.
                    return false;
                }

                _health = FileLoggerHealth.Degraded;
            }

            _rotationCount++;
            _archivesNeedCleanup = true;
            if (!TryCleanupArchivesUnderLock())
            {
                _archivesNeedCleanup = true;
                _health = FileLoggerHealth.Degraded;
            }
            else
            {
                _archivesNeedCleanup = false;
                if (_health != FileLoggerHealth.Degraded)
                {
                    _health = FileLoggerHealth.Healthy;
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
                    _health = FileLoggerHealth.Degraded;
                    return true;
                }

                RecordFailureUnderLock(FileLoggerFailureKind.Recovery, rollbackOpenFailure, writerUsable: false);
                return false;
            }
            catch (Exception exception)
            {
                RecordFailureUnderLock(FileLoggerFailureKind.Recovery, exception, writerUsable: false);
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

        private bool TryCleanupArchivesUnderLock()
        {
            try
            {
                string directory = Path.GetDirectoryName(_logFilePath);
                if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                {
                    return true;
                }

                var directoryInfo = new DirectoryInfo(directory);
                FileInfo[] candidates = directoryInfo.GetFiles(_archivePrefix + "*" + _archiveExtension);
                var ownedArchives = new List<FileInfo>(candidates.Length);
                for (int i = 0; i < candidates.Length; i++)
                {
                    if (IsOwnedArchive(candidates[i].Name))
                    {
                        ownedArchives.Add(candidates[i]);
                    }
                }

                ownedArchives.Sort(CompareArchiveAge);
                int deleteCount = ownedArchives.Count - _options.MaxArchiveFiles;
                bool allDeleted = true;
                for (int i = 0; i < deleteCount; i++)
                {
                    try
                    {
                        ownedArchives[i].Delete();
                    }
                    catch (Exception exception)
                    {
                        allDeleted = false;
                        _archiveCleanupFailures++;
                        RecordFailureUnderLock(FileLoggerFailureKind.ArchiveCleanup, exception, writerUsable: _writer != null);
                    }
                }

                return allDeleted;
            }
            catch (Exception exception)
            {
                _archiveCleanupFailures++;
                RecordFailureUnderLock(FileLoggerFailureKind.ArchiveCleanup, exception, writerUsable: _writer != null);
                return false;
            }
        }

        private bool IsOwnedArchive(string fileName)
        {
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
            if (TryParseArchiveToken(token))
            {
                return true;
            }

            int separator = token.LastIndexOf('-');
            if (separator <= 0 || separator == token.Length - 1)
            {
                return false;
            }

            int collisionSequence;
            if (!int.TryParse(
                    token.Substring(separator + 1),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out collisionSequence)
                || collisionSequence < 1)
            {
                return false;
            }

            return TryParseArchiveToken(token.Substring(0, separator));
        }

        private static bool TryParseArchiveToken(string token)
        {
            if (token == null || token.Length != 19)
            {
                return false;
            }

            long ticks;
            return long.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out ticks)
                && ticks >= DateTime.MinValue.Ticks
                && ticks <= DateTime.MaxValue.Ticks;
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
                RecordFailureUnderLock(FileLoggerFailureKind.Recovery, recoveryFailure, writerUsable: false);
                return false;
            }

            _recoveryCount++;
            _health = FileLoggerHealth.Degraded;
            _lastFlushTimestamp = now;
            return true;
        }

        private bool TryOpenWriterUnderLock(out Exception failure)
        {
            FileStream stream = null;
            StreamWriter writer = null;
            try
            {
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
            catch (Exception exception)
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
                }

                _stream = null;
                _writer = null;
                _currentFileBytes = 0L;
                _health = FileLoggerHealth.Faulted;
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
                catch (Exception exception)
                {
                    failure = exception;
                }
            }

            try
            {
                writer.Dispose();
            }
            catch (Exception exception)
            {
                if (failure == null)
                {
                    failure = exception;
                }
            }

            return failure == null;
        }

        private void HandleWriterFailureUnderLock(FileLoggerFailureKind kind, Exception exception)
        {
            CloseWriterUnderLock(flush: false, out _);
            RecordFailureUnderLock(kind, exception, writerUsable: false);
            TryRecoverWriterUnderLock(force: true);
        }

        private void RecordFailureUnderLock(FileLoggerFailureKind kind, Exception exception, bool writerUsable)
        {
            _lastFailure = kind;
            _lastFailureUtc = DateTime.UtcNow;
            _health = writerUsable ? FileLoggerHealth.Degraded : FileLoggerHealth.Faulted;
            TryReportDiagnosticUnderLock(kind, exception, null);
        }

        private void TryReportDiagnosticUnderLock(FileLoggerFailureKind kind, Exception exception, string detail)
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
                string failureType = exception == null ? "none" : exception.GetType().Name;
                string description = detail ?? "sink operation failed";
                string severity = kind == FileLoggerFailureKind.None ? "WARNING" : "ERROR";
                Console.Error.WriteLine(
                    "[" + severity + "] FileLogger: " + description
                    + "; kind=" + kind
                    + "; exception=" + failureType
                    + ".");
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

        private static int CompareArchiveAge(FileInfo left, FileInfo right)
        {
            int timeComparison = left.LastWriteTimeUtc.CompareTo(right.LastWriteTimeUtc);
            return timeComparison != 0
                ? timeComparison
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
            try
            {
                Console.Error.WriteLine(
                    "[ERROR] FileLogger: initialization failed; exception="
                    + exception.GetType().Name
                    + ".");
            }
            catch
            {
            }
        }
    }
}
