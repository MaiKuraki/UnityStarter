using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading.Tasks;

namespace CycloneGames.Logger
{
    public class FileLogger : ILogger
    {
        private readonly string _logFilePath;
        private readonly ConcurrentQueue<string> _logQueue = new ConcurrentQueue<string>();
        private readonly object _lock = new object();
        private const int MaxQueueSize = 100; // Maximum number of log entries to keep in memory
        private bool _isWriting = false;

        public FileLogger(string logFilePath)
        {
            if (string.IsNullOrWhiteSpace(logFilePath))
            {
                throw new ArgumentNullException(nameof(logFilePath), "Log file path cannot be null or empty.");
            }

            _logFilePath = logFilePath;

            try
            {
                // Get the directory path
                string directoryPath = Path.GetDirectoryName(_logFilePath);

                // Check if the directory path is valid and create it if it doesn't exist
                if (!string.IsNullOrWhiteSpace(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }

                // Optionally create the file or ensure it exists (zero-byte file)
                using (var stream = File.Create(_logFilePath))
                {
                    // File is created/opened. You can leave it empty.
                }
            }
            catch (Exception ex)
            {
                // Handle exceptions during directory creation or file creation
                Console.WriteLine($"Failed to create log file directory or file: {ex.Message}");
                throw; // Re-throwing the exception if you want to propagate the error
            }
        }


        public void LogInfo(string message)
        {
            EnqueueLog("INFO", message);
        }

        public void LogWarning(string message)
        {
            EnqueueLog("WARNING", message);
        }

        public void LogError(string message)
        {
            EnqueueLog("ERROR", message);
        }

        public void LogTrace(string message)
        {
            EnqueueLog("TRACE", message);
        }

        public void LogDebug(string message)
        {
            EnqueueLog("DEBUG", message);
        }

        public void LogFatal(string message)
        {
            EnqueueLog("FATAL", message);
        }

        private void EnqueueLog(string level, string message)
        {
            var logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}";

            // Add log entry to the queue
            _logQueue.Enqueue(logEntry);

            // If not currently writing, start the write process
            if (!_isWriting)
            {
                lock (_lock)
                {
                    if (!_isWriting)
                    {
                        _isWriting = true;
                        Task.Run(() => WriteLogsAsync());
                    }
                }
            }
        }

        private async Task WriteLogsAsync()
        {
            while (_logQueue.TryDequeue(out string logEntry))
            {
                try
                {
                    // Write the log entry to file asynchronously
                    await File.AppendAllTextAsync(_logFilePath, logEntry + Environment.NewLine);
                }
                catch (IOException ex)
                {
                    // Handle any I/O exceptions (e.g., log to Console or handle appropriately)
                    Console.WriteLine($"Failed to write log entry: {ex.Message}");
                }
                catch (Exception ex)
                {
                    // Handle any other exceptions
                    Console.WriteLine($"An unexpected error occurred: {ex.Message}");
                }
            }

            _isWriting = false;
        }
    }
}