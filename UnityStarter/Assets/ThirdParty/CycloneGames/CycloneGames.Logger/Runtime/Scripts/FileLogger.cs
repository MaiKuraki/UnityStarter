#if !UNITY_WEBGL || UNITY_EDITOR
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using CycloneGames.Logger.Util;

namespace CycloneGames.Logger
{
    /// <summary>
    /// Logs messages to a file with batched I/O.
    /// Thread-safety: writes are serialized via a private lock; queuing is handled by <see cref="CLogger"/>.
    /// Flush strategy: batches writes and flushes on Error/Fatal, every N messages, or after a time interval.
    /// </summary>
    public sealed class FileLogger : ILogger
    {
        private StreamWriter _writer;
        private readonly object _writeLock = new object();
        private volatile bool _disposed;
        private readonly string _logFilePath;
        private readonly FileLoggerOptions _options;
        private readonly char[] _buffer = new char[4096];

        private int _writesSinceFlush;
        private long _lastFlushTimestamp;
        private readonly long _flushIntervalTicks;

        private int _writesSinceMaintenanceCheck;
        private const int MaintenanceCheckInterval = 256;

        public FileLogger(string logFilePath, FileLoggerOptions options = null)
        {
            if (string.IsNullOrEmpty(logFilePath)) throw new ArgumentNullException(nameof(logFilePath));
            _logFilePath = logFilePath;
            _options = options ?? FileLoggerOptions.Default;
            _flushIntervalTicks = (long)(_options.FlushIntervalMs * 0.001 * Stopwatch.Frequency);

            try
            {
                var directory = Path.GetDirectoryName(logFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                InitializeWriter();
                _lastFlushTimestamp = Stopwatch.GetTimestamp();
                PerformMaintenanceIfNeeded();
            }
            catch (Exception ex)
            {
                _disposed = true;
                Console.Error.WriteLine($"[CRITICAL] FileLogger: Failed to initialize for path '{logFilePath}'. {ex.Message}");
                throw new InvalidOperationException($"Failed to initialize FileLogger for path '{logFilePath}'", ex);
            }
        }

        private void InitializeWriter()
        {
            var fileStream = new FileStream(_logFilePath, FileMode.Append, FileAccess.Write, FileShare.Read, bufferSize: 8192, useAsync: false);
            _writer = new StreamWriter(fileStream, Encoding.UTF8) { AutoFlush = false };
        }

        public void Log(LogMessage logMessage)
        {
            if (_disposed) return;

            StringBuilder sb = StringBuilderPool.Get();
            try
            {
                DateTimeUtil.FormatDateTimePrecise(logMessage.Timestamp, sb);
                sb.Append(" [");
                sb.Append(LogLevelStrings.Get(logMessage.Level));
                sb.Append("] ");

                if (!string.IsNullOrEmpty(logMessage.Category))
                {
                    sb.Append('[');
                    sb.Append(logMessage.Category);
                    sb.Append("] ");
                }

                if (logMessage.MessageBuilder != null)
                {
                    var mb = logMessage.MessageBuilder;
                    for (int i = 0; i < mb.Length; i++)
                    {
                        sb.Append(mb[i]);
                    }
                }
                else if (logMessage.OriginalMessage != null)
                {
                    sb.Append(logMessage.OriginalMessage);
                }

                if (!string.IsNullOrEmpty(logMessage.FilePath))
                {
                    sb.Append(" (at ");
                    string path = logMessage.FilePath;
                    int lastSep = -1;
                    for (int i = 0; i < path.Length; i++)
                    {
                        char c = path[i];
                        if (c == '/' || c == '\\') lastSep = i;
                    }
                    int start = lastSep + 1;
                    for (int i = start; i < path.Length; i++)
                    {
                        char c = path[i];
                        sb.Append(c == '\\' ? '/' : c);
                    }
                    sb.Append(':');
                    sb.Append(logMessage.LineNumber);
                    sb.Append(')');
                }
                sb.AppendLine();

                lock (_writeLock)
                {
                    if (_disposed) return;

                    int length = sb.Length;
                    int offset = 0;
                    while (offset < length)
                    {
                        int count = Math.Min(_buffer.Length, length - offset);
                        sb.CopyTo(offset, _buffer, 0, count);
                        _writer.Write(_buffer, 0, count);
                        offset += count;
                    }

                    // Flush on Error/Fatal immediately, or batch by count / time interval
                    _writesSinceFlush++;
                    bool shouldFlush = logMessage.Level >= LogLevel.Error
                        || _writesSinceFlush >= _options.FlushBatchSize;

                    if (!shouldFlush)
                    {
                        long now = Stopwatch.GetTimestamp();
                        shouldFlush = (now - _lastFlushTimestamp) >= _flushIntervalTicks;
                    }

                    if (shouldFlush)
                    {
                        _writer.Flush();
                        _writesSinceFlush = 0;
                        _lastFlushTimestamp = Stopwatch.GetTimestamp();
                    }

                    // Amortized maintenance: check file size every N writes instead of every write
                    if (_options.MaintenanceMode != FileMaintenanceMode.None)
                    {
                        _writesSinceMaintenanceCheck++;
                        if (_writesSinceMaintenanceCheck >= MaintenanceCheckInterval)
                        {
                            _writesSinceMaintenanceCheck = 0;
                            TryPerformMaintenanceQuick();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] FileLogger: Failed to write to log. {ex.Message}");
            }
            finally
            {
                StringBuilderPool.Return(sb);
            }
        }

        private void PerformMaintenanceIfNeeded()
        {
            if (_options.MaintenanceMode == FileMaintenanceMode.None) return;
            try
            {
                var fi = new FileInfo(_logFilePath);
                if (!fi.Exists) return;
                if (fi.Length <= _options.MaxFileBytes) return;

                switch (_options.MaintenanceMode)
                {
                    case FileMaintenanceMode.WarnOnly:
                        Console.Error.WriteLine($"[WARNING] FileLogger: Log file exceeded {_options.MaxFileBytes} bytes. Path: {_logFilePath}");
                        break;
                    case FileMaintenanceMode.Rotate:
                        RotateFiles(fi);
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] FileLogger: Maintenance failed. {ex.Message}");
            }
        }

        private void TryPerformMaintenanceQuick()
        {
            try
            {
                var length = (_writer.BaseStream?.Length) ?? 0L;
                if (length > _options.MaxFileBytes)
                {
                    PerformMaintenanceIfNeeded();
                }
            }
            catch { /* ignore lightweight check errors */ }
        }

        private void RotateFiles(FileInfo current)
        {
            try
            {
                _writer.Flush();
                _writer.Dispose();
            }
            catch { }

            var timestamp = DateTime.Now.ToString(_options.ArchiveTimestampFormat);
            string archivePath = Path.Combine(current.DirectoryName!, Path.GetFileNameWithoutExtension(current.Name) + "_" + timestamp + current.Extension);

            try
            {
                File.Move(_logFilePath, archivePath);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] FileLogger: Rotation rename failed. {ex.Message}");
            }

            InitializeWriter();
            _lastFlushTimestamp = Stopwatch.GetTimestamp();

            try
            {
                var dir = current.Directory;
                if (dir != null)
                {
                    var baseName = Path.GetFileNameWithoutExtension(current.Name);
                    var ext = current.Extension;
                    var archives = dir.GetFiles(baseName + "_*" + ext);
                    Array.Sort(archives, (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
                    for (int i = _options.MaxArchiveFiles; i < archives.Length; i++)
                    {
                        try { archives[i].Delete(); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[WARNING] FileLogger: Archive cleanup failed. {ex.Message}");
            }
        }

        public void Dispose()
        {
            if (_disposed) return;

            lock (_writeLock)
            {
                if (_disposed) return;
                _disposed = true;

                try
                {
                    _writer?.Flush();
                    _writer?.Dispose();
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[ERROR] FileLogger: Failed to dispose writer. {ex.Message}");
                }
            }
        }
    }
}
#endif
