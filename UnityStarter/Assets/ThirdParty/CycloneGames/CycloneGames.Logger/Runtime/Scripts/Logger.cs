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
    public sealed class CLogger : ICLogger
    {
        private static Func<CLogger, ILogProcessor> _processorFactory = owner => new ThreadedLogProcessor(owner);
        private static Func<DateTime> _timestampProvider = () => DateTime.Now;
        private static readonly object _instanceLock = new();
        private static volatile bool _suppressGlobalStaticLogging;
        private static CLogger _instance;

        public static CLogger Instance
        {
            get
            {
                var instance = _instance;
                if (instance != null) return instance;

                lock (_instanceLock)
                {
                    _suppressGlobalStaticLogging = false;
                    return _instance ??= new CLogger(_processorFactory, _timestampProvider);
                }
            }
        }

        internal static bool TryGetInstance(out CLogger instance)
        {
            instance = _instance;
            return instance != null;
        }

        private List<ILogger> _loggers = new();
        private volatile ILogger[] _loggerSnapshot = Array.Empty<ILogger>();
        private readonly HashSet<Type> _loggerTypes = new();
        private readonly ReaderWriterLockSlim _loggersLock = new(LockRecursionPolicy.NoRecursion);
        private readonly object _dispatchStateLock = new();
        private int _activeDispatchCount;
        private const int LoggerDisposeWaitMs = 1000;

        // Processing strategy decoupled from platform specifics; no Unity macros here.
        private readonly ILogProcessor _processor;
        private readonly Func<DateTime> _instanceTimestampProvider;

        private volatile LogLevel _currentLogLevel = LogLevel.Info; // Default log level.
        private volatile LogFilter _currentLogFilter = LogFilter.LogAll; // Default filter.
        private readonly HashSet<string> _whiteList = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _blackList = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _filterLock = new(); // Protects filter mode and lists.
        private volatile bool _disposed;
#if UNITY_EDITOR
        private readonly int _mainThreadId;
#endif

        public CLogger()
            : this(_processorFactory, _timestampProvider)
        {
        }

        public CLogger(LoggerProcessingOptions processingOptions)
            : this(owner => new ThreadedLogProcessor(owner, LoggerProcessingOptions.CreateValidated(processingOptions)), _timestampProvider)
        {
        }

        public CLogger(Func<CLogger, ILogProcessor> processorFactory)
            : this(processorFactory, _timestampProvider)
        {
        }

        public CLogger(Func<CLogger, ILogProcessor> processorFactory, Func<DateTime> timestampProvider)
        {
#if UNITY_EDITOR
            _mainThreadId = Environment.CurrentManagedThreadId;
#endif
            _instanceTimestampProvider = timestampProvider ?? (() => DateTime.Now);
            try
            {
                _processor = (processorFactory ?? (o => new ThreadedLogProcessor(o)))(this);
            }
            catch (ArgumentException)
            {
                throw;
            }
            catch
            {
                _processor = new SingleThreadLogProcessor(this);
            }

            LogMessagePool.Prewarm();
            Util.StringBuilderPool.Prewarm();
        }

        /// <summary>
        /// Configure the processor factory before the first access to Instance to fully decouple platform specifics.
        /// Advanced: intended for infrastructure code. Most projects should prefer
        /// <see cref="ConfigureSingleThreadedProcessing"/> or <see cref="ConfigureThreadedProcessing"/>.
        /// </summary>
        internal static bool ConfigureProcessorFactory(Func<CLogger, ILogProcessor> factory)
        {
            if (factory == null) return false;

            lock (_instanceLock)
            {
                if (_instance != null)
                {
                    Console.Error.WriteLine("[WARNING] CLogger: Processing configuration ignored because the global instance already exists.");
                    return false;
                }

                _processorFactory = factory;
                return true;
            }
        }

        /// <summary>
        /// Force single-threaded processing (manual Pump). Call this before first use of Instance.
        /// Suitable for platforms without background threads (e.g., Web/WASM).
        /// </summary>
        public static void ConfigureSingleThreadedProcessing(LoggerProcessingOptions options = null)
        {
            var capturedOptions = LoggerProcessingOptions.CreateValidated(options);
            if (ConfigureProcessorFactory(o => new SingleThreadLogProcessor(o, capturedOptions)))
            {
                LoggerUpdater.Configure(capturedOptions);
            }
        }

        /// <summary>
        /// Force threaded processing. Call this before first use of Instance.
        /// </summary>
        public static void ConfigureThreadedProcessing(LoggerProcessingOptions options = null)
        {
            var capturedOptions = LoggerProcessingOptions.CreateValidated(options);
            if (ConfigureProcessorFactory(o => new ThreadedLogProcessor(o, capturedOptions)))
            {
                LoggerUpdater.Configure(capturedOptions);
            }
        }

        public static void ConfigureTimestampProvider(Func<DateTime> timestampProvider)
        {
            if (timestampProvider == null) throw new ArgumentNullException(nameof(timestampProvider));

            lock (_instanceLock)
            {
                if (_instance != null)
                {
                    Console.Error.WriteLine("[WARNING] CLogger: Timestamp provider configuration ignored because the global instance already exists.");
                    return;
                }

                _timestampProvider = timestampProvider;
            }
        }

        /// <summary>
        /// Disposes the global logger instance and allows a later access to create a fresh one.
        /// </summary>
        public static void Shutdown()
        {
            CLogger instance;
            lock (_instanceLock)
            {
                instance = _instance;
                _instance = null;
                _suppressGlobalStaticLogging = false;
            }

            instance?.Dispose();
            LoggerUpdater.Shutdown();
        }

        internal static void ConfigureGlobalStaticLoggingSuppressed(bool suppress)
        {
            lock (_instanceLock)
            {
                if (_instance != null) return;
                _suppressGlobalStaticLogging = suppress;
            }
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
                if (!_loggers.Contains(logger))
                {
                    _loggers.Add(logger);
                    _loggerTypes.Add(logger.GetType());
                    PublishLoggerSnapshot();
                }
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
                PublishLoggerSnapshot();
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
                    RebuildLoggerTypes();
                    PublishLoggerSnapshot();
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
                _loggerSnapshot = Array.Empty<ILogger>();
            }
            finally { _loggersLock.ExitWriteLock(); }

            if (!WaitForActiveDispatches(LoggerDisposeWaitMs))
            {
                Console.Error.WriteLine("[WARNING] CLogger: ClearLoggers skipped sink disposal because dispatch did not become idle.");
                return;
            }

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
        [UnityEngine.HideInCallstack]
        public void Log(LogLevel level, string message, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
            => EnqueueMessage(level, message, category, filePath, lineNumber, memberName);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [UnityEngine.HideInCallstack]
        public void Log(LogLevel level, Action<StringBuilder> messageBuilder, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
            => EnqueueMessage(level, messageBuilder, category, filePath, lineNumber, memberName);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [UnityEngine.HideInCallstack]
        public void Log<T>(LogLevel level, T state, Action<T, StringBuilder> messageBuilder, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
            => EnqueueMessage(level, state, messageBuilder, category, filePath, lineNumber, memberName);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ShouldLog(LogLevel logLevel, string category)
        {
            if (logLevel < _currentLogLevel) return false;

            LogFilter currentFilter = _currentLogFilter;
            if (currentFilter == LogFilter.LogAll) return true;

            lock (_filterLock)
            {
                currentFilter = _currentLogFilter;
                switch (currentFilter)
                {
                    case LogFilter.LogWhiteList when string.IsNullOrEmpty(category): return false;
                    case LogFilter.LogNoBlackList when string.IsNullOrEmpty(category): return true;
                    case LogFilter.LogWhiteList: return _whiteList.Contains(category);
                    case LogFilter.LogNoBlackList: return !_blackList.Contains(category);
                    default: return true;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool HasLoggers()
        {
            return _loggerSnapshot.Length != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryGetGlobalInstanceForLogging(out CLogger logger)
        {
            logger = _instance;
            if (logger != null) return true;
            if (_suppressGlobalStaticLogging) return false;

            logger = Instance;
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [UnityEngine.HideInCallstack]
        internal static void LogGlobal(LogLevel level, string message, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
        {
            if (TryGetGlobalInstanceForLogging(out var logger)) logger.EnqueueMessage(level, message, category, filePath, lineNumber, memberName);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [UnityEngine.HideInCallstack]
        internal static void LogGlobal(LogLevel level, Action<StringBuilder> messageBuilder, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
        {
            if (TryGetGlobalInstanceForLogging(out var logger)) logger.EnqueueMessage(level, messageBuilder, category, filePath, lineNumber, memberName);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [UnityEngine.HideInCallstack]
        internal static void LogGlobal<T>(LogLevel level, T state, Action<T, StringBuilder> messageBuilder, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
        {
            if (TryGetGlobalInstanceForLogging(out var logger)) logger.EnqueueMessage(level, state, messageBuilder, category, filePath, lineNumber, memberName);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [UnityEngine.HideInCallstack]
        public static void LogTrace(string message, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
        {
            if (TryGetGlobalInstanceForLogging(out var logger)) logger.EnqueueMessage(LogLevel.Trace, message, category, filePath, lineNumber, memberName);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [UnityEngine.HideInCallstack]
        public static void LogDebug(string message, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
        {
            if (TryGetGlobalInstanceForLogging(out var logger)) logger.EnqueueMessage(LogLevel.Debug, message, category, filePath, lineNumber, memberName);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [UnityEngine.HideInCallstack]
        public static void LogInfo(string message, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
        {
            if (TryGetGlobalInstanceForLogging(out var logger)) logger.EnqueueMessage(LogLevel.Info, message, category, filePath, lineNumber, memberName);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [UnityEngine.HideInCallstack]
        public static void LogWarning(string message, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
        {
            if (TryGetGlobalInstanceForLogging(out var logger)) logger.EnqueueMessage(LogLevel.Warning, message, category, filePath, lineNumber, memberName);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [UnityEngine.HideInCallstack]
        public static void LogError(string message, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
        {
            if (TryGetGlobalInstanceForLogging(out var logger)) logger.EnqueueMessage(LogLevel.Error, message, category, filePath, lineNumber, memberName);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [UnityEngine.HideInCallstack]
        public static void LogFatal(string message, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
        {
            if (TryGetGlobalInstanceForLogging(out var logger)) logger.EnqueueMessage(LogLevel.Fatal, message, category, filePath, lineNumber, memberName);
        }

        // Builder-based overloads to avoid intermediate string allocations when logging is disabled or to minimize GC.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [UnityEngine.HideInCallstack]
        public static void LogTrace(Action<StringBuilder> messageBuilder, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
        {
            if (TryGetGlobalInstanceForLogging(out var logger)) logger.EnqueueMessage(LogLevel.Trace, messageBuilder, category, filePath, lineNumber, memberName);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [UnityEngine.HideInCallstack]
        public static void LogDebug(Action<StringBuilder> messageBuilder, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
        {
            if (TryGetGlobalInstanceForLogging(out var logger)) logger.EnqueueMessage(LogLevel.Debug, messageBuilder, category, filePath, lineNumber, memberName);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [UnityEngine.HideInCallstack]
        public static void LogInfo(Action<StringBuilder> messageBuilder, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
        {
            if (TryGetGlobalInstanceForLogging(out var logger)) logger.EnqueueMessage(LogLevel.Info, messageBuilder, category, filePath, lineNumber, memberName);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [UnityEngine.HideInCallstack]
        public static void LogWarning(Action<StringBuilder> messageBuilder, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
        {
            if (TryGetGlobalInstanceForLogging(out var logger)) logger.EnqueueMessage(LogLevel.Warning, messageBuilder, category, filePath, lineNumber, memberName);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [UnityEngine.HideInCallstack]
        public static void LogError(Action<StringBuilder> messageBuilder, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
        {
            if (TryGetGlobalInstanceForLogging(out var logger)) logger.EnqueueMessage(LogLevel.Error, messageBuilder, category, filePath, lineNumber, memberName);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [UnityEngine.HideInCallstack]
        public static void LogFatal(Action<StringBuilder> messageBuilder, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
        {
            if (TryGetGlobalInstanceForLogging(out var logger)) logger.EnqueueMessage(LogLevel.Fatal, messageBuilder, category, filePath, lineNumber, memberName);
        }

        [UnityEngine.HideInCallstack]
        internal void EnqueueMessage(LogLevel level, string originalMessage, string category, string filePath, int lineNumber, string memberName)
        {
            if (_disposed) return;
            if (!ShouldLog(level, category)) return;
            if (!HasLoggers()) return;

            LogMessage logEntry = null;
            try
            {
                logEntry = LogMessagePool.Get();
                logEntry.Initialize(_instanceTimestampProvider(), level, originalMessage, null, category, filePath, lineNumber, memberName);
                DispatchEditorUnityLoggersImmediate(logEntry);
                _processor.Enqueue(logEntry);
                logEntry = null;
            }
            catch
            {
                if (logEntry != null) LogMessagePool.Return(logEntry);
                throw;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [UnityEngine.HideInCallstack]
        internal void EnqueueMessage(LogLevel level, Action<StringBuilder> messageBuilder, string category, string filePath, int lineNumber, string memberName)
        {
            if (_disposed) return;
            if (!ShouldLog(level, category)) return;
            if (!HasLoggers()) return;

            StringBuilder sb = StringBuilderPool.Get();
            LogMessage logEntry = null;
            bool ownsBuilder = true;
            try
            {
                messageBuilder?.Invoke(sb);
                logEntry = LogMessagePool.Get();
                logEntry.Initialize(_instanceTimestampProvider(), level, null, sb, category, filePath, lineNumber, memberName);
                ownsBuilder = false;
                DispatchEditorUnityLoggersImmediate(logEntry);
                _processor.Enqueue(logEntry);
                logEntry = null;
            }
            catch
            {
                if (logEntry != null)
                {
                    LogMessagePool.Return(logEntry);
                }
                else if (ownsBuilder)
                {
                    StringBuilderPool.Return(sb);
                }
                throw;
            }
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [UnityEngine.HideInCallstack]
        public static void LogTrace<T>(T state, Action<T, StringBuilder> messageBuilder, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
        {
            if (TryGetGlobalInstanceForLogging(out var logger)) logger.EnqueueMessage(LogLevel.Trace, state, messageBuilder, category, filePath, lineNumber, memberName);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [UnityEngine.HideInCallstack]
        public static void LogDebug<T>(T state, Action<T, StringBuilder> messageBuilder, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
        {
            if (TryGetGlobalInstanceForLogging(out var logger)) logger.EnqueueMessage(LogLevel.Debug, state, messageBuilder, category, filePath, lineNumber, memberName);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [UnityEngine.HideInCallstack]
        public static void LogInfo<T>(T state, Action<T, StringBuilder> messageBuilder, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
        {
            if (TryGetGlobalInstanceForLogging(out var logger)) logger.EnqueueMessage(LogLevel.Info, state, messageBuilder, category, filePath, lineNumber, memberName);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [UnityEngine.HideInCallstack]
        public static void LogWarning<T>(T state, Action<T, StringBuilder> messageBuilder, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
        {
            if (TryGetGlobalInstanceForLogging(out var logger)) logger.EnqueueMessage(LogLevel.Warning, state, messageBuilder, category, filePath, lineNumber, memberName);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [UnityEngine.HideInCallstack]
        public static void LogError<T>(T state, Action<T, StringBuilder> messageBuilder, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
        {
            if (TryGetGlobalInstanceForLogging(out var logger)) logger.EnqueueMessage(LogLevel.Error, state, messageBuilder, category, filePath, lineNumber, memberName);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [UnityEngine.HideInCallstack]
        public static void LogFatal<T>(T state, Action<T, StringBuilder> messageBuilder, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
        {
            if (TryGetGlobalInstanceForLogging(out var logger)) logger.EnqueueMessage(LogLevel.Fatal, state, messageBuilder, category, filePath, lineNumber, memberName);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [UnityEngine.HideInCallstack]
        internal void EnqueueMessage<T>(LogLevel level, T state, Action<T, StringBuilder> messageBuilder, string category, string filePath, int lineNumber, string memberName)
        {
            if (_disposed) return;
            if (!ShouldLog(level, category)) return;
            if (!HasLoggers()) return;

            StringBuilder sb = StringBuilderPool.Get();
            LogMessage logEntry = null;
            bool ownsBuilder = true;
            try
            {
                messageBuilder?.Invoke(state, sb);
                logEntry = LogMessagePool.Get();
                logEntry.Initialize(_instanceTimestampProvider(), level, null, sb, category, filePath, lineNumber, memberName);
                ownsBuilder = false;
                DispatchEditorUnityLoggersImmediate(logEntry);
                _processor.Enqueue(logEntry);
                logEntry = null;
            }
            catch
            {
                if (logEntry != null)
                {
                    LogMessagePool.Return(logEntry);
                }
                else if (ownsBuilder)
                {
                    StringBuilderPool.Return(sb);
                }
                throw;
            }
        }

        internal void DispatchToLoggers(LogMessage logMessage)
        {
            var loggers = _loggerSnapshot;
            BeginDispatch();
            try
            {
                for (int i = 0; i < loggers.Length; i++)
                {
#if UNITY_EDITOR
                    if (logMessage.EditorUnityConsoleLogged && loggers[i] is UnityLogger) continue;
#endif
                    try
                    {
                        loggers[i].Log(logMessage);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[CRITICAL] CLogger: Logger {loggers[i].GetType().Name} failed. {ex.Message}");
                    }
                }
            }
            finally
            {
                EndDispatch();
            }
        }

        private void PublishLoggerSnapshot()
        {
            _loggerSnapshot = _loggers.Count == 0 ? Array.Empty<ILogger>() : _loggers.ToArray();
        }

        private void RebuildLoggerTypes()
        {
            _loggerTypes.Clear();
            for (int i = 0; i < _loggers.Count; i++)
            {
                _loggerTypes.Add(_loggers[i].GetType());
            }
        }

        private void BeginDispatch()
        {
            Interlocked.Increment(ref _activeDispatchCount);
        }

        private void EndDispatch()
        {
            if (Interlocked.Decrement(ref _activeDispatchCount) != 0) return;

            lock (_dispatchStateLock)
            {
                Monitor.PulseAll(_dispatchStateLock);
            }
        }

        private bool WaitForActiveDispatches(int timeoutMs)
        {
            if (Volatile.Read(ref _activeDispatchCount) == 0) return true;

            lock (_dispatchStateLock)
            {
                if (timeoutMs < 0)
                {
                    while (Volatile.Read(ref _activeDispatchCount) != 0)
                    {
                        Monitor.Wait(_dispatchStateLock);
                    }

                    return true;
                }

                int startTick = Environment.TickCount;
                while (Volatile.Read(ref _activeDispatchCount) != 0)
                {
                    int elapsed = unchecked(Environment.TickCount - startTick);
                    int remaining = timeoutMs - elapsed;
                    if (remaining <= 0) return false;
                    Monitor.Wait(_dispatchStateLock, remaining);
                }
            }

            return true;
        }

        [UnityEngine.HideInCallstack]
        private void DispatchEditorUnityLoggersImmediate(LogMessage logMessage)
        {
#if UNITY_EDITOR
            if (Environment.CurrentManagedThreadId != _mainThreadId) return;

            var loggers = _loggerSnapshot;
            BeginDispatch();
            try
            {
                for (int i = 0; i < loggers.Length; i++)
                {
                    if (loggers[i] is UnityLogger unityLogger)
                    {
                        unityLogger.LogImmediate(logMessage);
                        logMessage.EditorUnityConsoleLogged = true;
                    }
                }
            }
            finally
            {
                EndDispatch();
            }
#endif
        }

        /// <summary>
        /// Processes queued log messages.
        /// - Single-threaded processing: call regularly (e.g., once per frame) to avoid stalls.
        /// - Threaded processing: this is a no-op and can be left in place for portability.
        /// </summary>
        /// <param name="maxItems">Upper bound to the number of messages processed in this call.</param>
        public void Pump(int maxItems = 256) => _processor.Pump(maxItems);

        public LogProcessingStatistics GetProcessingStatistics()
        {
            return _processor is ILogProcessorDiagnostics diagnostics
                ? diagnostics.GetStatistics()
                : default;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            bool disposedGlobalInstance = false;

            Console.WriteLine("[INFO] CLogger: Dispose called. Shutting down...");

            _processor.Dispose();
            if (!(_processor is ILogProcessorDiagnostics diagnostics) || diagnostics.IsStopped)
            {
                ClearLoggers();
                _loggersLock.Dispose();
            }
            else
            {
                Console.Error.WriteLine("[WARNING] CLogger: Processor did not stop; logger sinks were left undisposed to avoid use-after-dispose.");
            }

            lock (_instanceLock)
            {
                if (ReferenceEquals(_instance, this))
                {
                    _instance = null;
                    disposedGlobalInstance = true;
                }
            }

            if (disposedGlobalInstance)
            {
                LoggerUpdater.Shutdown();
            }

            Console.WriteLine("[INFO] CLogger: Shutdown complete.");
        }
    }
}
