using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CycloneGames.Logger
{
    public enum LogLevel
    {
        Trace,
        Debug,
        Info,
        Warning,
        Error,
        Fatal
    }

    public enum LogFilter
    {
        LogAll,
        LogWhiteList,
        LogNoBlackList
    }

    public sealed class CLogger : IDisposable
    {
        private static readonly Lazy<CLogger> _instance = new Lazy<CLogger>(() => new CLogger());
        public static CLogger Instance => _instance.Value;

        private readonly List<ILogger> _loggers = new List<ILogger>();
        private readonly ReaderWriterLockSlim _loggersLock = new ReaderWriterLockSlim();

        private readonly BlockingCollection<LogMessage> _messageQueue = new BlockingCollection<LogMessage>(new ConcurrentQueue<LogMessage>());
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private readonly Task _processingTask;

        private LogLevel _currentLogLevel = LogLevel.Info;
        private LogFilter _currentFilter = LogFilter.LogAll;
        private readonly HashSet<string> _whiteList = new HashSet<string>();
        private readonly HashSet<string> _blackList = new HashSet<string>();
        private readonly object _filterLock = new object();

        private CLogger()
        {
            _processingTask = Task.Run(ProcessQueue);
        }

        public void SetLogLevel(LogLevel level) => _currentLogLevel = level;

        public void SetLogFilter(LogFilter filter)
        {
            lock (_filterLock)
            {
                _currentFilter = filter;
            }
        }

        public void AddLogger(ILogger logger)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));

            _loggersLock.EnterWriteLock();
            try
            {
                _loggers.Add(logger);
            }
            finally
            {
                _loggersLock.ExitWriteLock();
            }
        }

        public void AddLoggerUnique(ILogger logger)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));

            _loggersLock.EnterWriteLock();
            try
            {
                Type loggerType = logger.GetType();
                bool alreadyExists = false;
                foreach (var existingLogger in _loggers)
                {
                    if (existingLogger.GetType() == loggerType)
                    {
                        alreadyExists = true;
                        break;
                    }
                }
                if (!alreadyExists)
                {
                    _loggers.Add(logger);
                }
            }
            finally
            {
                _loggersLock.ExitWriteLock();
            }
        }

        public void RemoveLogger(ILogger logger)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));

            _loggersLock.EnterWriteLock();
            try
            {
                _loggers.Remove(logger);
            }
            finally
            {
                _loggersLock.ExitWriteLock();
            }
        }

        public void ClearLoggers()
        {
            _loggersLock.EnterWriteLock();
            try
            {
                foreach (var logger in _loggers)
                {
                    logger.Dispose();
                }
                _loggers.Clear();
            }
            finally
            {
                _loggersLock.ExitWriteLock();
            }
        }

        public void AddToWhiteList(string category)
        {
            lock (_filterLock)
            {
                _whiteList.Add(category);
            }
        }

        public void AddToBlackList(string category)
        {
            lock (_filterLock)
            {
                _blackList.Add(category);
            }
        }

        private bool ShouldLog(LogLevel logLevel, string category)
        {
            if (logLevel < _currentLogLevel) return false;

            lock (_filterLock)
            {
                return _currentFilter switch
                {
                    LogFilter.LogAll => true,
                    LogFilter.LogWhiteList => _whiteList.Contains(category),
                    LogFilter.LogNoBlackList => !_blackList.Contains(category),
                    _ => false
                };
            }
        }

        private static string FormatMessage(string message, string category)
        {
            if (string.IsNullOrEmpty(category)) return message;

            var sb = new StringBuilder();
            sb.Append("[");
            sb.Append(category);
            sb.Append("] ");
            sb.Append(message);
            return sb.ToString();
        }

        public static void LogTrace(string message, string category = "") => Instance.EnqueueMessage(LogLevel.Trace, message, category);
        public static void LogDebug(string message, string category = "") => Instance.EnqueueMessage(LogLevel.Debug, message, category);
        public static void LogInfo(string message, string category = "") => Instance.EnqueueMessage(LogLevel.Info, message, category);
        public static void LogWarning(string message, string category = "") => Instance.EnqueueMessage(LogLevel.Warning, message, category);
        public static void LogError(string message, string category = "") => Instance.EnqueueMessage(LogLevel.Error, message, category);
        public static void LogFatal(string message, string category = "") => Instance.EnqueueMessage(LogLevel.Fatal, message, category);

        private void EnqueueMessage(LogLevel level, string message, string category)
        {
            if (!ShouldLog(level, category)) return;

            var formattedMessage = FormatMessage(message, category);
            _messageQueue.Add(new LogMessage(level, formattedMessage));
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
                        foreach (var logger in _loggers)
                        {
                            try
                            {
                                switch (logMessage.Level)
                                {
                                    case LogLevel.Trace: logger.LogTrace(logMessage.Message); break;
                                    case LogLevel.Debug: logger.LogDebug(logMessage.Message); break;
                                    case LogLevel.Info: logger.LogInfo(logMessage.Message); break;
                                    case LogLevel.Warning: logger.LogWarning(logMessage.Message); break;
                                    case LogLevel.Error: logger.LogError(logMessage.Message); break;
                                    case LogLevel.Fatal: logger.LogFatal(logMessage.Message); break;
                                }
                            }
                            catch { /* Prevent logger exceptions from breaking the loop */ }
                        }
                    }
                    finally
                    {
                        _loggersLock.ExitReadLock();
                    }
                }
            }
            catch (OperationCanceledException) { /* Shutdown requested */ }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _messageQueue.CompleteAdding();

            try { _processingTask.Wait(); }
            catch { /* Ignore */ }

            _cts.Dispose();
            _messageQueue.Dispose();
            ClearLoggers();
        }

        private readonly struct LogMessage
        {
            public readonly LogLevel Level;
            public readonly string Message;

            public LogMessage(LogLevel level, string message)
            {
                Level = level;
                Message = message;
            }
        }
    }
}