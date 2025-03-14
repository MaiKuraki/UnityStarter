using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        private readonly object _lock = new object();

        private LogLevel _currentLogLevel = LogLevel.Info;
        private LogFilter _currentFilter = LogFilter.LogAll;

        private HashSet<string> _whiteList = new HashSet<string>();
        private HashSet<string> _blackList = new HashSet<string>();

        private readonly ConcurrentQueue<LogMessage> _messageQueue = new ConcurrentQueue<LogMessage>();
        private readonly SemaphoreSlim _signal = new SemaphoreSlim(0);
        private readonly CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        private readonly Task _processingTask;

        private CLogger()
        {
            _processingTask = Task.Run(ProcessQueueAsync);
        }

        /// <summary>
        /// Set the current log level.
        /// </summary>
        public void SetLogLevel(LogLevel level)
        {
            _currentLogLevel = level;
        }

        /// <summary>
        /// Set the current log filter.
        /// </summary>
        public void SetLogFilter(LogFilter filter)
        {
            _currentFilter = filter;
        }

        public bool ContainsLoggerOfType<T>() where T : ILogger
        {
            lock (_lock)
            {
                foreach (var logger in _loggers)
                {
                    if (logger is T)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public void AddLogger(ILogger logger)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            lock (_lock)
            {
                _loggers.Add(logger);
            }
        }

        public void AddLoggerUnique(ILogger logger)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            lock (_lock)
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
        }

        public void RemoveLogger(ILogger logger)
        {
            if (logger == null) throw new ArgumentNullException(nameof(logger));
            lock (_lock)
            {
                _loggers.Remove(logger);
            }
        }

        public void ClearLoggers()
        {
            lock (_lock)
            {
                foreach (var logger in _loggers)
                {
                    if (logger is IDisposable disposable)
                    {
                        disposable.Dispose();
                    }
                }
                _loggers.Clear();
            }
        }

        public void AddToWhiteList(string category)
        {
            _whiteList.Add(category);
        }

        public void AddToBlackList(string category)
        {
            _blackList.Add(category);
        }

        private bool IsLevelAllowed(LogLevel logLevel)
        {
            return logLevel >= _currentLogLevel;
        }

        private bool ShouldLogByCategory(string category)
        {
            switch (_currentFilter)
            {
                case LogFilter.LogAll:
                    return true;
                case LogFilter.LogWhiteList:
                    return _whiteList.Contains(category);
                case LogFilter.LogNoBlackList:
                    return !_blackList.Contains(category);
                default:
                    return false;
            }
        }

        private bool ShouldLog(LogLevel logLevel, string category)
        {
            return IsLevelAllowed(logLevel) && ShouldLogByCategory(category);
        }

        private string FormatMessage(string message, string category)
        {
            return string.IsNullOrEmpty(category) ? message : $"[{category}] {message}";
        }

        public static void LogTrace(string message, string category = "")
        {
            Instance.EnqueueMessage(LogLevel.Trace, message, category);
        }

        public static void LogDebug(string message, string category = "")
        {
            Instance.EnqueueMessage(LogLevel.Debug, message, category);
        }

        public static void LogInfo(string message, string category = "")
        {
            Instance.EnqueueMessage(LogLevel.Info, message, category);
        }

        public static void LogWarning(string message, string category = "")
        {
            Instance.EnqueueMessage(LogLevel.Warning, message, category);
        }

        public static void LogError(string message, string category = "")
        {
            Instance.EnqueueMessage(LogLevel.Error, message, category);
        }

        public static void LogFatal(string message, string category = "")
        {
            Instance.EnqueueMessage(LogLevel.Fatal, message, category);
        }

        private void EnqueueMessage(LogLevel level, string message, string category)
        {
            if (!ShouldLog(level, category)) return;
            var formattedMessage = FormatMessage(message, category);
            _messageQueue.Enqueue(new LogMessage(level, formattedMessage));
            _signal.Release();
        }

        private async Task ProcessQueueAsync()
        {
            try
            {
                while (!_cancellationTokenSource.IsCancellationRequested)
                {
                    await _signal.WaitAsync(_cancellationTokenSource.Token);

                    while (_messageQueue.TryDequeue(out var logMessage))
                    {
                        List<ILogger> loggersSnapshot;
                        lock (_lock)
                        {
                            loggersSnapshot = new List<ILogger>(_loggers);
                        }

                        foreach (var logger in loggersSnapshot)
                        {
                            try
                            {
                                switch (logMessage.Level)
                                {
                                    case LogLevel.Trace:
                                        logger.LogTrace(logMessage.Message);
                                        break;
                                    case LogLevel.Debug:
                                        logger.LogDebug(logMessage.Message);
                                        break;
                                    case LogLevel.Info:
                                        logger.LogInfo(logMessage.Message);
                                        break;
                                    case LogLevel.Warning:
                                        logger.LogWarning(logMessage.Message);
                                        break;
                                    case LogLevel.Error:
                                        logger.LogError(logMessage.Message);
                                        break;
                                    case LogLevel.Fatal:
                                        logger.LogFatal(logMessage.Message);
                                        break;
                                }
                            }
                            catch
                            {

                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {

            }
        }

        public void Dispose()
        {
            _cancellationTokenSource.Cancel();
            try
            {
                _processingTask.Wait();
            }
            catch (Exception)
            {
                throw;
            }
            finally
            {
                _signal.Dispose();
            }
            _cancellationTokenSource.Dispose();

            ClearLoggers();
        }

        private class LogMessage
        {
            public LogLevel Level { get; }
            public string Message { get; }

            public LogMessage(LogLevel level, string message)
            {
                Level = level;
                Message = message;
            }
        }
    }
}