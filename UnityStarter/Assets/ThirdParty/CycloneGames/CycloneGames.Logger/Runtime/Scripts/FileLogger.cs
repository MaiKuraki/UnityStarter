using System; 
using System.Collections.Concurrent; 
using System.IO; 
using System.Text; 
using System.Threading; 
 
namespace CycloneGames.Logger 
{ 
    public sealed class FileLogger : ILogger, IDisposable 
    { 
        private const int MaxBatchSize = 50; 
        private const int FlushIntervalMs = 1000; 
 
        private readonly string _logFilePath; 
        private readonly ConcurrentQueue<string> _logQueue = new(); 
        private readonly StreamWriter _writer; 
        private readonly Timer _flushTimer; 
        private volatile bool _disposed; 
 
        public FileLogger(string logFilePath) 
        { 
            _logFilePath = logFilePath ?? throw new ArgumentNullException(nameof(logFilePath)); 
 
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
 
        public void LogInfo(string message) => EnqueueLog("INFO", message); 
        public void LogWarning(string message) => EnqueueLog("WARNING", message); 
        public void LogError(string message) => EnqueueLog("ERROR", message); 
        public void LogTrace(string message) => EnqueueLog("TRACE", message); 
        public void LogDebug(string message) => EnqueueLog("DEBUG", message); 
        public void LogFatal(string message) => EnqueueLog("FATAL", message); 
 
        private void EnqueueLog(string level, string message) 
        { 
            if (_disposed) return; 
 
            var sb = new StringBuilder(); 
            sb.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")); 
            sb.Append(" ["); 
            sb.Append(level); 
            sb.Append("] "); 
            sb.Append(message); 
            var logEntry = sb.ToString(); 
 
            _logQueue.Enqueue(logEntry); 
            // flush immediately
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
            catch { /* Prevent unhandled exceptions in timer callback */ } 
        } 
 
        private void FlushLogs(object state) 
        { 
            TryFlush(); 
        } 
 
        public void Dispose() 
        { 
            if (_disposed) return; 
 
            _disposed = true; 
            _flushTimer.Dispose(); 
 
            // Final flush 
            while (_logQueue.TryDequeue(out var logEntry)) 
            { 
                _writer.WriteLine(logEntry); 
            } 
            _writer.Flush(); 
            _writer.Dispose(); 
        } 
    } 
} 