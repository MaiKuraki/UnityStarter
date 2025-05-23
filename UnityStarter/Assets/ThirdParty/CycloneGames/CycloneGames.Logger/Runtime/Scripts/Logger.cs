using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace CycloneGames.Logger
{
    /// <summary>
    /// Central logging manager. Dispatches log messages to registered ILogger instances.
    /// Provides static methods for easy logging.
    /// </summary>
    public sealed class CLogger : IDisposable
    {
        private static readonly Lazy<CLogger> _instance = new(() => new CLogger());
        public static CLogger Instance => _instance.Value;

        private readonly List<ILogger> _loggers = new();
        private readonly ReaderWriterLockSlim _loggersLock = new(LockRecursionPolicy.NoRecursion);

        private readonly BlockingCollection<LogMessage> _messageQueue = new(new ConcurrentQueue<LogMessage>());
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _processingTask;

        private volatile LogLevel _currentLogLevel = LogLevel.Info; // Default log level.
        private volatile LogFilter _currentLogFilter = LogFilter.LogAll; // Default filter.
        private readonly HashSet<string> _whiteList = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _blackList = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _filterLock = new(); // Protects filter mode and lists.

        private CLogger()
        {
            _processingTask = Task.Factory.StartNew(ProcessQueue, _cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        public void SetLogLevel(LogLevel level) => _currentLogLevel = level;
        public LogLevel GetLogLevel() => _currentLogLevel;

        public void SetLogFilter(LogFilter filter)
        {
            lock (_filterLock) { _currentLogFilter = filter; }
        }

        public void AddLogger(ILogger logger)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            _loggersLock.EnterWriteLock();
            try
            {
                if (!_loggers.Contains(logger)) { _loggers.Add(logger); }
            }
            finally { _loggersLock.ExitWriteLock(); }
        }

        /// <summary>
        /// Adds a logger only if no logger of the same exact type already exists.
        /// </summary>
        public void AddLoggerUnique(ILogger logger)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            _loggersLock.EnterWriteLock();
            try
            {
                Type loggerType = logger.GetType();
                bool exists = false;
                // This iteration is acceptable for a small number of loggers.
                // For a very large number, a HashSet<Type> could track added types.
                for (int i = 0; i < _loggers.Count; i++)
                {
                    if (_loggers[i].GetType() == loggerType)
                    {
                        exists = true;
                        break;
                    }
                }
                if (!exists) { _loggers.Add(logger); }
            }
            finally { _loggersLock.ExitWriteLock(); }
        }

        public void RemoveLogger(ILogger logger)
        {
            if (logger == null) return;
            _loggersLock.EnterWriteLock();
            try
            {
                _loggers.Remove(logger);
            }
            finally { _loggersLock.ExitWriteLock(); }
            // Consider whether CLogger owns the logger and should dispose it.
            // logger.Dispose(); // If CLogger is responsible for logger lifecycle.
        }

        /// <summary>
        /// Removes all loggers and disposes them.
        /// </summary>
        public void ClearLoggers()
        {
            _loggersLock.EnterWriteLock();
            List<ILogger> toDispose;
            try
            {
                toDispose = new List<ILogger>(_loggers); // Create a copy for safe iteration.
                _loggers.Clear();
            }
            finally { _loggersLock.ExitWriteLock(); }

            for (int i = 0; i < toDispose.Count; i++)
            {
                try { toDispose[i].Dispose(); }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[ERROR] CLogger: Failed to dispose logger {toDispose[i].GetType().Name}. {ex.Message}");
                }
            }
        }

        public void AddToWhiteList(string category)
        {
            if (string.IsNullOrEmpty(category)) return;
            lock (_filterLock) { _whiteList.Add(category); }
        }
        public void RemoveFromWhiteList(string category)
        {
            if (string.IsNullOrEmpty(category)) return;
            lock (_filterLock) { _whiteList.Remove(category); }
        }

        public void AddToBlackList(string category)
        {
            if (string.IsNullOrEmpty(category)) return;
            lock (_filterLock) { _blackList.Add(category); }
        }
        public void RemoveFromBlackList(string category)
        {
            if (string.IsNullOrEmpty(category)) return;
            lock (_filterLock) { _blackList.Remove(category); }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ShouldLog(LogLevel logLevel, string category)
        {
            if (logLevel < _currentLogLevel) return false; // Primary level filter.

            // Quick path for LogAll or no category.
            LogFilter currentFilter = _currentLogFilter; // Read volatile field once.
            if (currentFilter == LogFilter.LogAll || string.IsNullOrEmpty(category)) return true;

            lock (_filterLock) // Lock only if category filtering is needed.
            {
                switch (currentFilter) // Use the local copy of _currentLogFilter
                {
                    case LogFilter.LogWhiteList: return _whiteList.Contains(category);
                    case LogFilter.LogNoBlackList: return !_blackList.Contains(category);
                    // LogAll already handled, default is redundant but safe.
                    default: return true; 
                }
            }
        }

        // Static logging methods
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LogTrace(string message, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
            => Instance.EnqueueMessage(LogLevel.Trace, message, category, filePath, lineNumber, memberName);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LogDebug(string message, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
            => Instance.EnqueueMessage(LogLevel.Debug, message, category, filePath, lineNumber, memberName);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LogInfo(string message, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
            => Instance.EnqueueMessage(LogLevel.Info, message, category, filePath, lineNumber, memberName);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LogWarning(string message, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
            => Instance.EnqueueMessage(LogLevel.Warning, message, category, filePath, lineNumber, memberName);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LogError(string message, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
            => Instance.EnqueueMessage(LogLevel.Error, message, category, filePath, lineNumber, memberName);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LogFatal(string message, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
            => Instance.EnqueueMessage(LogLevel.Fatal, message, category, filePath, lineNumber, memberName);

        private void EnqueueMessage(LogLevel level, string originalMessage, string category, string filePath, int lineNumber, string memberName)
        {
            // Filter check happens before any significant work.
            if (!ShouldLog(level, category)) return;

            var logEntry = new LogMessage(
                DateTime.Now, // Timestamp captured as close to call as possible.
                level,
                originalMessage,
                category,
                filePath,
                lineNumber,
                memberName
            );
            
            // Non-blocking add; BlockingCollection handles concurrency.
            // Can throw if CompleteAdding() has been called and collection is full, though unlikely with ConcurrentQueue.
            if (!_messageQueue.IsAddingCompleted)
            {
                try
                {
                    _messageQueue.Add(logEntry);
                }
                catch (InvalidOperationException) { /* Queue is completed, ignore. */ }
            }
        }
        
        private void ProcessQueue()
        {
            try
            {
                // GetConsumingEnumerable will block until items are available or collection is marked as complete.
                foreach (var logMessage in _messageQueue.GetConsumingEnumerable(_cts.Token))
                {
                    _loggersLock.EnterReadLock();
                    try
                    {
                        // Iterate over current loggers. List is not modified while read lock is held.
                        for (int i = 0; i < _loggers.Count; i++)
                        {
                            var logger = _loggers[i];
                            try
                            {
                                switch (logMessage.Level)
                                {
                                    case LogLevel.Trace: logger.LogTrace(logMessage); break;
                                    case LogLevel.Debug: logger.LogDebug(logMessage); break;
                                    case LogLevel.Info: logger.LogInfo(logMessage); break;
                                    case LogLevel.Warning: logger.LogWarning(logMessage); break;
                                    case LogLevel.Error: logger.LogError(logMessage); break;
                                    case LogLevel.Fatal: logger.LogFatal(logMessage); break;
                                }
                            }
                            catch (Exception ex)
                            {
                                // Log sink itself failed. Report to console.
                                Console.Error.WriteLine($"[CRITICAL] CLogger: Logger {logger.GetType().Name} failed. {ex.Message}");
                            }
                        }
                    }
                    finally
                    {
                        _loggersLock.ExitReadLock();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when _cts.Cancel() is called during Dispose.
                Console.WriteLine("[INFO] CLogger: Processing task cancelled.");
            }
            catch (Exception ex)
            {
                // Unexpected error in the processing loop.
                Console.Error.WriteLine($"[CRITICAL] CLogger: ProcessQueue critical error. {ex}");
            }
        }

        public void Dispose()
        {
            if (_cts.IsCancellationRequested) return;
            Console.WriteLine("[INFO] CLogger: Dispose called. Shutting down...");

            _messageQueue.CompleteAdding(); // Stop new messages from being enqueued.
            _cts.Cancel(); // Signal cancellation to ProcessQueue.

            try
            {
                // Wait for the processing task to finish, with a timeout.
                if (!_processingTask.Wait(TimeSpan.FromSeconds(5)))
                {
                    Console.Error.WriteLine("[WARNING] CLogger: Processing task timeout on shutdown.");
                }
            }
            catch (AggregateException ae)
            {
                ae.Handle(ex => ex is OperationCanceledException); // Expect OperationCanceledException.
            }
            catch (Exception ex) // Other exceptions during task wait.
            {
                 Console.Error.WriteLine($"[ERROR] CLogger: Error during task shutdown. {ex.Message}");
            }

            _cts.Dispose();
            _messageQueue.Dispose();
            _loggersLock.Dispose();
            
            ClearLoggers(); // Dispose all registered loggers.
            Console.WriteLine("[INFO] CLogger: Shutdown complete.");
        }
    }
}