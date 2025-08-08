#if !UNITY_WEBGL || UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using CycloneGames.Logger.Util;

namespace CycloneGames.Logger
{
    /// <summary>
    /// Logs messages to a file. This logger is simplified to be thread-safe for synchronous writing,
    /// as the asynchronous queuing is now handled centrally by CLogger.
    /// </summary>
    public sealed class FileLogger : ILogger
    {
        private readonly StreamWriter _writer;
        private readonly object _writeLock = new object(); // Lock to ensure thread-safe writes.
        private volatile bool _disposed;

        public FileLogger(string logFilePath)
        {
            if (string.IsNullOrEmpty(logFilePath)) throw new ArgumentNullException(nameof(logFilePath));

            try
            {
                var directory = Path.GetDirectoryName(logFilePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Use a larger buffer for FileStream for better IO performance.
                // AutoFlush is set to true to ensure logs are written immediately, which is simpler and safer
                // now that CLogger handles the background processing.
                var fileStream = new FileStream(logFilePath, FileMode.Append, FileAccess.Write, FileShare.Read, bufferSize: 4096, useAsync: false);
                _writer = new StreamWriter(fileStream, Encoding.UTF8) { AutoFlush = true };
            }
            catch (Exception ex)
            {
                _disposed = true; // Mark as disposed to prevent operations.
                Console.Error.WriteLine($"[CRITICAL] FileLogger: Failed to initialize for path '{logFilePath}'. {ex.Message}");
                throw new InvalidOperationException($"Failed to initialize FileLogger for path '{logFilePath}'", ex);
            }
        }

        public void LogTrace(LogMessage logMessage) => WriteLog(logMessage);
        public void LogDebug(LogMessage logMessage) => WriteLog(logMessage);
        public void LogInfo(LogMessage logMessage) => WriteLog(logMessage);
        public void LogWarning(LogMessage logMessage) => WriteLog(logMessage);
        public void LogError(LogMessage logMessage) => WriteLog(logMessage);
        public void LogFatal(LogMessage logMessage) => WriteLog(logMessage);

        private void WriteLog(LogMessage logMessage)
        {
            if (_disposed) return;

            StringBuilder sb = StringBuilderPool.Get();
            try
            {
                // Format the log message using the pooled StringBuilder.
                DateTimeUtil.FormatDateTimePrecise(logMessage.Timestamp, sb);
                sb.Append(" [");
                sb.Append(LogLevelStrings.Get(logMessage.Level)); // Optimized level to string
                sb.Append("] ");

                if (!string.IsNullOrEmpty(logMessage.Category))
                {
                    sb.Append("[");
                    sb.Append(logMessage.Category);
                    sb.Append("] ");
                }
                sb.Append(logMessage.OriginalMessage);
                
                // File/line info can be very useful in file logs.
                if (!string.IsNullOrEmpty(logMessage.FilePath))
                {
                    sb.Append($" (at {Path.GetFileName(logMessage.FilePath)}:{logMessage.LineNumber})");
                }
                sb.AppendLine();

                // Lock ensures that writes from different threads are serialized.
                lock (_writeLock)
                {
                    if (!_disposed)
                    {
                        _writer.Write(sb.ToString());
                    }
                }
            }
            catch (Exception ex)
            {
                // Fallback error logging for issues during write.
                Console.Error.WriteLine($"[ERROR] FileLogger: Failed to write to log. {ex.Message}");
            }
            finally
            {
                StringBuilderPool.Return(sb);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            
            lock (_writeLock)
            {
                if (_disposed) return;
                _disposed = true;
                
                // StreamWriter.Dispose() also disposes the underlying stream.
                try
                {
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
