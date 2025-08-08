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

        private List<ILogger> _loggers = new();
        private readonly HashSet<Type> _loggerTypes = new();
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
            Type loggerType = logger.GetType();

            _loggersLock.EnterWriteLock();
            try
            {
                if (_loggerTypes.Contains(loggerType)) return;
                
                _loggers.Add(logger);
                _loggerTypes.Add(loggerType);
            }
            finally { _loggersLock.ExitWriteLock(); }
        }

        public void RemoveLogger(ILogger logger)
        {
            if (logger == null) return;
            _loggersLock.EnterWriteLock();
            try
            {
                if (_loggers.Remove(logger))
                {
                    _loggerTypes.Remove(logger.GetType());
                }
            }
            finally { _loggersLock.ExitWriteLock(); }
        }

        /// <summary>
        /// Removes all loggers and disposes them. This operation is optimized to avoid extra list allocations.
        /// </summary>
        public void ClearLoggers()
        {
            List<ILogger> toDispose;
            _loggersLock.EnterWriteLock();
            try
            {
                toDispose = _loggers;
                _loggers = new List<ILogger>();
                _loggerTypes.Clear();
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
            if (logLevel < _currentLogLevel) return false;

            LogFilter currentFilter = _currentLogFilter;
            if (currentFilter == LogFilter.LogAll || string.IsNullOrEmpty(category)) return true;

            lock (_filterLock)
            {
                switch (currentFilter)
                {
                    case LogFilter.LogWhiteList: return _whiteList.Contains(category);
                    case LogFilter.LogNoBlackList: return !_blackList.Contains(category);
                    default: return true;
                }
            }
        }

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
            if (!ShouldLog(level, category)) return;
            if (_messageQueue.IsAddingCompleted) return;

            try
            {
                var logEntry = LogMessagePool.Get();
                logEntry.Initialize(DateTime.Now, level, originalMessage, category, filePath, lineNumber, memberName);
                _messageQueue.Add(logEntry);
            }
            catch (InvalidOperationException) { /* Ignore if shutting down. */ }
        }

        private void ProcessQueue()
        {
            try
            {
                foreach (var logMessage in _messageQueue.GetConsumingEnumerable(_cts.Token))
                {
                    _loggersLock.EnterReadLock();
                    try
                    {
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
                                Console.Error.WriteLine($"[CRITICAL] CLogger: Logger {logger.GetType().Name} failed. {ex.Message}");
                            }
                        }
                    }
                    finally
                    {
                        _loggersLock.ExitReadLock();
                    }
                    
                    LogMessagePool.Return(logMessage);
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("[INFO] CLogger: Processing task cancelled.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[CRITICAL] CLogger: ProcessQueue critical error. {ex}");
            }
        }

        public void Dispose()
        {
            if (_cts.IsCancellationRequested) return;
            Console.WriteLine("[INFO] CLogger: Dispose called. Shutting down...");

            _messageQueue.CompleteAdding();
            _cts.Cancel();

            try
            {
                if (!_processingTask.Wait(TimeSpan.FromSeconds(2)))
                {
                    Console.Error.WriteLine("[WARNING] CLogger: Processing task timeout on shutdown.");
                }
            }
            catch (AggregateException ae)
            {
                ae.Handle(ex => ex is OperationCanceledException);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ERROR] CLogger: Error during task shutdown. {ex.Message}");
            }

            ClearLoggers();

            _cts.Dispose();
            _messageQueue.Dispose();
            _loggersLock.Dispose();
            
            Console.WriteLine("[INFO] CLogger: Shutdown complete.");
        }
    }
}
