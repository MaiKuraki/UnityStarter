using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using CycloneGames.Logger.Util;

namespace CycloneGames.Logger // Ensure namespace matches if it contains LogMessage
{
    /// <summary>
    /// Logs messages to a file, with asynchronous batch writing.
    /// </summary>
    public sealed class FileLogger : ILogger, IDisposable
    {
        private const int MaxBatchSize = 100;       // Max log entries per flush.
        private const int FlushIntervalMs = 1000;   // Interval for timed flush.

        private readonly StreamWriter _writer;
        private readonly Timer _flushTimer;
        private readonly ConcurrentQueue<LogMessage> _logQueue = new(); // Changed from ConcurrentQueue<string>
        private volatile bool _disposed;

        public FileLogger(string logFilePath)
        {
            if (string.IsNullOrEmpty(logFilePath)) throw new ArgumentNullException(nameof(logFilePath));

            StreamWriter tempWriter = null;
            Timer tempTimer = null;
            try
            {
                var directory = Path.GetDirectoryName(logFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Use a larger buffer for FileStream for potentially better IO performance.
                var fileStream = new FileStream(logFilePath, FileMode.Append, FileAccess.Write, FileShare.Read, bufferSize: 4096, useAsync: true);
                tempWriter = new StreamWriter(fileStream, Encoding.UTF8) { AutoFlush = false };
                _writer = tempWriter;

                tempTimer = new Timer(TimerFlushLogs, null, FlushIntervalMs, FlushIntervalMs);
                _flushTimer = tempTimer;
            }
            catch (Exception ex)
            {
                _disposed = true; // Mark as disposed to prevent operations.
                tempTimer?.Dispose();
                tempWriter?.Dispose(); // Disposes underlying stream too.
                // Fallback critical error logging.
                Console.Error.WriteLine($"[CRITICAL] FileLogger: Failed to initialize for path '{logFilePath}'. {ex.Message}");
                throw new InvalidOperationException($"Failed to initialize FileLogger for path '{logFilePath}'", ex);
            }
        }

        public void LogTrace(in LogMessage logMessage) => EnqueueLogMessage(logMessage);
        public void LogDebug(in LogMessage logMessage) => EnqueueLogMessage(logMessage);
        public void LogInfo(in LogMessage logMessage) => EnqueueLogMessage(logMessage);
        public void LogWarning(in LogMessage logMessage) => EnqueueLogMessage(logMessage);
        public void LogError(in LogMessage logMessage) => EnqueueLogMessage(logMessage);
        public void LogFatal(in LogMessage logMessage) => EnqueueLogMessage(logMessage);

        private void EnqueueLogMessage(in LogMessage logMessage)
        {
            if (_disposed) return;
            _logQueue.Enqueue(logMessage); // Enqueue the struct directly.
        }

        private void TimerFlushLogs(object state) => FlushQueue();

        private void FlushQueue()
        {
            if (_disposed || _logQueue.IsEmpty) return;

            StringBuilder batchBuilder = StringBuilderPool.Get();
            try
            {
                int processedCount = 0;
                while (processedCount < MaxBatchSize && _logQueue.TryDequeue(out var logMessage)) // Dequeue LogMessage struct
                {
                    // Format the LogMessage here
                    DateTimeUtil.FormatDateTimePrecise(logMessage.Timestamp, batchBuilder);
                    batchBuilder.Append(" [");
                    batchBuilder.Append(LogLevelStrings.Get(logMessage.Level)); // Optimized level to string
                    batchBuilder.Append("] ");

                    if (!string.IsNullOrEmpty(logMessage.Category))
                    {
                        batchBuilder.Append("[");
                        batchBuilder.Append(logMessage.Category);
                        batchBuilder.Append("] ");
                    }
                    batchBuilder.Append(logMessage.OriginalMessage);

                    // Optionally include file/line info in file logs
                    // if (!string.IsNullOrEmpty(logMessage.FilePath))
                    // {
                    //     batchBuilder.Append($" (at {Path.GetFileName(logMessage.FilePath)}:{logMessage.LineNumber})");
                    // }
                    batchBuilder.AppendLine(); // Each log entry on a new line in the batch
                    processedCount++;
                }

                if (processedCount > 0)
                {
                    _writer.Write(batchBuilder.ToString()); // Write the entire batch
                    _writer.Flush(); // Ensure data is written to the OS. Underlying stream might still buffer.
                }
            }
            catch (Exception ex)
            {
                // Fallback error logging for issues during flush.
                Console.Error.WriteLine($"[ERROR] FileLogger: Failed to write to log. {ex.Message}");
            }
            finally
            {
                StringBuilderPool.Return(batchBuilder);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _flushTimer?.Change(Timeout.Infinite, Timeout.Infinite); // Stop the timer
            _flushTimer?.Dispose();

            FlushQueue(); // Attempt to flush any remaining logs.

            try
            {
                _writer?.Flush();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] FileLogger: Failed to flush during dispose. {ex.Message}");
            }
            finally
            {
                // StreamWriter.Dispose() also disposes the underlying stream.
                _writer?.Dispose();
            }
        }
    }
}