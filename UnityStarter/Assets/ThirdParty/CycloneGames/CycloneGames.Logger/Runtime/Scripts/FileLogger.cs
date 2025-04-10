using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace CycloneGames.Logger
{
    public sealed class FileLogger : ILogger, IDisposable
    {
        private const int MaxBatchSize = 100;
        private const int FlushIntervalMs = 1000;
        private const int InitialStringBuilderCapacity = 256;
        private readonly StreamWriter _writer;
        private readonly Timer _flushTimer;
        private readonly ConcurrentQueue<string> _logQueue = new();
        private volatile bool _disposed;
        private readonly string _logFilePath;
        private readonly StringBuilder _stringBuilder = new StringBuilder(InitialStringBuilderCapacity);

        public FileLogger(string logFilePath)
        {
            _logFilePath = logFilePath ?? throw new ArgumentNullException(nameof(logFilePath));

            try
            {
                var directory = Path.GetDirectoryName(logFilePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                _writer = new StreamWriter(
                    new FileStream(logFilePath, FileMode.Append, FileAccess.Write, FileShare.Read),
                    Encoding.UTF8)
                {
                    AutoFlush = false
                };

                _flushTimer = new Timer(FlushLogs, null, FlushIntervalMs, FlushIntervalMs);
            }
            catch (Exception ex)
            {
                Dispose();
                throw new InvalidOperationException("Failed to initialize FileLogger", ex);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LogTrace(in string message) => EnqueueLog("TRACE", message);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LogDebug(in string message) => EnqueueLog("DEBUG", message);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LogInfo(in string message) => EnqueueLog("INFO", message);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LogWarning(in string message) => EnqueueLog("WARNING", message);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LogError(in string message) => EnqueueLog("ERROR", message);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LogFatal(in string message) => EnqueueLog("FATAL", message);

        private void EnqueueLog(in string level, in string message)
        {
            if (_disposed) return;

            lock (_stringBuilder)
            {
                _stringBuilder.Clear();
                _stringBuilder.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                _stringBuilder.Append(" [");
                _stringBuilder.Append(level);
                _stringBuilder.Append("] ");
                _stringBuilder.Append(message);

                _logQueue.Enqueue(_stringBuilder.ToString());
            }

            TryFlush();
        }

        private void TryFlush()
        {
            if (_disposed || _logQueue.IsEmpty) return;

            try
            {
                int count = 0;
                while (count < MaxBatchSize && _logQueue.TryDequeue(out var logEntry))
                {
                    _writer.WriteLine(logEntry);
                    count++;
                }

                if (count > 0)
                {
                    _writer.Flush();
                }
            }
            catch { /* Swallow exceptions to prevent logging system crashes */ }
        }

        private void FlushLogs(object state) => TryFlush();

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _flushTimer.Dispose();

            // Flush remaining logs 
            while (_logQueue.TryDequeue(out var logEntry))
            {
                _writer.WriteLine(logEntry);
            }

            _writer.Flush();
            _writer.Dispose();
        }
    }
}