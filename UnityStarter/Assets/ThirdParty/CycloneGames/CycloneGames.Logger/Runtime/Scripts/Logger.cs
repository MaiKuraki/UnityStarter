using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using CycloneGames.Logger.Util;

namespace CycloneGames.Logger
{
    /// <summary>
    /// Central logging manager.
    ///
    /// Responsibilities:
    /// - Provides static convenience APIs (LogTrace..LogFatal) with string and builder overloads
    /// - Filters by severity and category before allocating work
    /// - Queues messages into a pluggable processing strategy (threaded or single-threaded)
    /// - Dispatches to registered <see cref="ILogger"/> implementations
    ///
    /// Performance/GC:
    /// - Builder overloads avoid intermediate string allocations when logging is disabled
    /// - Messages are pooled via <see cref="LogMessagePool"/>
    /// - Formatting helpers reuse <see cref="Util.StringBuilderPool"/>
    ///
    /// Thread-safety:
    /// - Logger registration is protected by a <see cref="ReaderWriterLockSlim"/>
    /// - Dispatch occurs inside a read-lock to minimize contention
    ///
    /// Platform notes:
    /// - Single-threaded processing requires calling <see cref="Pump"/> regularly (e.g., once per frame)
    /// - Threaded processing ignores Pump() and drains in a background worker
    /// </summary>
    public sealed class CLogger : IDisposable
    {
        private static Func<CLogger, ILogProcessor> _processorFactory = owner => new ThreadedLogProcessor(owner);
        private static readonly Lazy<CLogger> _instance = new(() => new CLogger());
        public static CLogger Instance => _instance.Value;

        private List<ILogger> _loggers = new();
        private readonly HashSet<Type> _loggerTypes = new();
        private readonly ReaderWriterLockSlim _loggersLock = new(LockRecursionPolicy.NoRecursion);

        // Processing strategy decoupled from platform specifics; no Unity macros here.
        private readonly ILogProcessor _processor;

        private volatile LogLevel _currentLogLevel = LogLevel.Info; // Default log level.
        private volatile LogFilter _currentLogFilter = LogFilter.LogAll; // Default filter.
        private readonly HashSet<string> _whiteList = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _blackList = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _filterLock = new(); // Protects filter mode and lists.

        private CLogger()
        {
            try
            {
                _processor = (_processorFactory ?? (o => new ThreadedLogProcessor(o)))(this);
            }
            catch
            {
                // Fallback for platforms that do not support background threads
                _processor = new SingleThreadLogProcessor(this);
            }
        }

        /// <summary>
        /// Configure the processor factory before the first access to Instance to fully decouple platform specifics.
        /// Advanced: intended for infrastructure code. Most projects should prefer
        /// <see cref="ConfigureSingleThreadedProcessing"/> or <see cref="ConfigureThreadedProcessing"/>.
        /// </summary>
        internal static void ConfigureProcessorFactory(Func<CLogger, ILogProcessor> factory)
        {
            if (factory != null) _processorFactory = factory;
        }

        /// <summary>
        /// Force single-threaded processing (manual Pump). Call this before first use of Instance.
        /// Suitable for platforms without background threads (e.g., Web/WASM).
        /// </summary>
        public static void ConfigureSingleThreadedProcessing()
        {
            _processorFactory = o => new SingleThreadLogProcessor(o);
        }

        /// <summary>
        /// Force threaded processing. Call this before first use of Instance.
        /// </summary>
        public static void ConfigureThreadedProcessing()
        {
            _processorFactory = o => new ThreadedLogProcessor(o);
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

        // Builder-based overloads to avoid intermediate string allocations when logging is disabled or to minimize GC.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LogTrace(Action<StringBuilder> messageBuilder, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
            => Instance.EnqueueMessage(LogLevel.Trace, messageBuilder, category, filePath, lineNumber, memberName);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LogDebug(Action<StringBuilder> messageBuilder, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
            => Instance.EnqueueMessage(LogLevel.Debug, messageBuilder, category, filePath, lineNumber, memberName);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LogInfo(Action<StringBuilder> messageBuilder, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
            => Instance.EnqueueMessage(LogLevel.Info, messageBuilder, category, filePath, lineNumber, memberName);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LogWarning(Action<StringBuilder> messageBuilder, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
            => Instance.EnqueueMessage(LogLevel.Warning, messageBuilder, category, filePath, lineNumber, memberName);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LogError(Action<StringBuilder> messageBuilder, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
            => Instance.EnqueueMessage(LogLevel.Error, messageBuilder, category, filePath, lineNumber, memberName);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LogFatal(Action<StringBuilder> messageBuilder, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
            => Instance.EnqueueMessage(LogLevel.Fatal, messageBuilder, category, filePath, lineNumber, memberName);

        private void EnqueueMessage(LogLevel level, string originalMessage, string category, string filePath, int lineNumber, string memberName)
        {
            if (!ShouldLog(level, category)) return;

            try
            {
                var logEntry = LogMessagePool.Get();
                logEntry.Initialize(DateTime.Now, level, originalMessage, null, category, filePath, lineNumber, memberName);
                _processor.Enqueue(logEntry);
            }
            catch (InvalidOperationException) { /* Ignore if shutting down. */ }
        }

        // Zero/min-GC-friendly overloads that build messages only when logging is enabled.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnqueueMessage(LogLevel level, Action<StringBuilder> messageBuilder, string category, string filePath, int lineNumber, string memberName)
        {
            if (!ShouldLog(level, category)) return;

            StringBuilder sb = StringBuilderPool.Get();
            try
            {
                messageBuilder?.Invoke(sb);
                var logEntry = LogMessagePool.Get();
                logEntry.Initialize(DateTime.Now, level, null, sb, category, filePath, lineNumber, memberName);
                _processor.Enqueue(logEntry);
            }
            catch
            {
                StringBuilderPool.Return(sb);
                throw;
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LogTrace<T>(T state, Action<T, StringBuilder> messageBuilder, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
            => Instance.EnqueueMessage(LogLevel.Trace, state, messageBuilder, category, filePath, lineNumber, memberName);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LogDebug<T>(T state, Action<T, StringBuilder> messageBuilder, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
            => Instance.EnqueueMessage(LogLevel.Debug, state, messageBuilder, category, filePath, lineNumber, memberName);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LogInfo<T>(T state, Action<T, StringBuilder> messageBuilder, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
            => Instance.EnqueueMessage(LogLevel.Info, state, messageBuilder, category, filePath, lineNumber, memberName);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LogWarning<T>(T state, Action<T, StringBuilder> messageBuilder, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
            => Instance.EnqueueMessage(LogLevel.Warning, state, messageBuilder, category, filePath, lineNumber, memberName);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LogError<T>(T state, Action<T, StringBuilder> messageBuilder, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
            => Instance.EnqueueMessage(LogLevel.Error, state, messageBuilder, category, filePath, lineNumber, memberName);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void LogFatal<T>(T state, Action<T, StringBuilder> messageBuilder, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
            => Instance.EnqueueMessage(LogLevel.Fatal, state, messageBuilder, category, filePath, lineNumber, memberName);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnqueueMessage<T>(LogLevel level, T state, Action<T, StringBuilder> messageBuilder, string category, string filePath, int lineNumber, string memberName)
        {
            if (!ShouldLog(level, category)) return;

            StringBuilder sb = StringBuilderPool.Get();
            try
            {
                messageBuilder?.Invoke(state, sb);
                var logEntry = LogMessagePool.Get();
                logEntry.Initialize(DateTime.Now, level, null, sb, category, filePath, lineNumber, memberName);
                _processor.Enqueue(logEntry);
            }
            catch
            {
                StringBuilderPool.Return(sb);
                throw;
            }
        }

        internal void DispatchToLoggers(LogMessage logMessage)
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
        }

        /// <summary>
        /// Processes queued log messages.
        /// - Single-threaded processing: call regularly (e.g., once per frame) to avoid stalls.
        /// - Threaded processing: this is a no-op and can be left in place for portability.
        /// </summary>
        /// <param name="maxItems">Upper bound to the number of messages processed in this call.</param>
        public void Pump(int maxItems = 256) => _processor.Pump(maxItems);

        public void Dispose()
        {
            Console.WriteLine("[INFO] CLogger: Dispose called. Shutting down...");

            _processor.Dispose();
            ClearLoggers();
            _loggersLock.Dispose();

            Console.WriteLine("[INFO] CLogger: Shutdown complete.");
        }
    }
}