using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using CycloneGames.Logger.Util;

namespace CycloneGames.Logger
{
    /// <summary>
    /// Bounded logging facade and sink owner. The static facade is optional; callers may
    /// explicitly construct an instance through <see cref="CLoggerFactory"/>.
    /// </summary>
    public sealed class CLogger : ICLogger
    {
        private sealed class SinkRegistration
        {
            private const int RetiredMask = 1;
            private const int UsageIncrement = 2;

            internal readonly ILogger Sink;
            internal int ActiveCountReleased;
            internal int ConsecutiveFailures;
            internal int QuarantinedByFailure;

            private readonly object _quiescenceLock = new object();
            private int _usageState;

            internal SinkRegistration(ILogger sink)
            {
                Sink = sink;
            }

            internal bool TryEnter()
            {
                while (true)
                {
                    int current = Volatile.Read(ref _usageState);
                    if ((current & RetiredMask) != 0)
                    {
                        return false;
                    }

                    if (Interlocked.CompareExchange(ref _usageState, current + UsageIncrement, current) == current)
                    {
                        return true;
                    }
                }
            }

            internal bool IsRetired => (Volatile.Read(ref _usageState) & RetiredMask) != 0;

            internal void Exit()
            {
                int current = Interlocked.Add(ref _usageState, -UsageIncrement);
                if ((current & ~RetiredMask) != 0)
                {
                    return;
                }

                lock (_quiescenceLock)
                {
                    Monitor.PulseAll(_quiescenceLock);
                }
            }

            internal bool Retire()
            {
                while (true)
                {
                    int current = Volatile.Read(ref _usageState);
                    if ((current & RetiredMask) != 0)
                    {
                        return false;
                    }

                    if (Interlocked.CompareExchange(ref _usageState, current | RetiredMask, current) == current)
                    {
                        return true;
                    }
                }
            }

            internal bool WaitForQuiescence(int timeoutMs)
            {
                int startTick = Environment.TickCount;
                lock (_quiescenceLock)
                {
                    while ((Volatile.Read(ref _usageState) & ~RetiredMask) != 0)
                    {
                        int remaining = timeoutMs < 0
                            ? Timeout.Infinite
                            : timeoutMs - unchecked(Environment.TickCount - startTick);
                        if (remaining <= 0)
                        {
                            return false;
                        }

                        Monitor.Wait(_quiescenceLock, remaining);
                    }

                    return true;
                }
            }
        }

        private sealed class SinkReferenceComparer : IEqualityComparer<ILogger>
        {
            internal static readonly SinkReferenceComparer Instance = new SinkReferenceComparer();

            public bool Equals(ILogger left, ILogger right)
            {
                return ReferenceEquals(left, right);
            }

            public int GetHashCode(ILogger value)
            {
                return RuntimeHelpers.GetHashCode(value);
            }
        }

        private const int DefaultSinkQuiescenceTimeoutMs = 1000;
        private const int MaxOwnedSinks = 256;
        private const int SinkDisposeAttemptCount = 3;

        private static readonly object InstanceLock = new object();
        private static Func<CLogger, LoggerProcessingOptions, ILogProcessor> _processorFactory = CreatePlatformDefaultProcessor;
        private static LoggerProcessingOptions _globalProcessingOptions = new LoggerProcessingOptions();
        private static Func<DateTime> _timestampProvider = () => DateTime.UtcNow;
        private static volatile CLogger _instance;
        private static volatile bool _shutdownInProgress;
        private static volatile bool _suppressGlobalStaticLogging;
        private static volatile bool _globalCreationBlocked;

        private readonly ReaderWriterLockSlim _sinksLock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
        private readonly List<SinkRegistration> _sinks = new List<SinkRegistration>();
        private readonly Dictionary<ILogger, SinkRegistration> _retiredRegistrations =
            new Dictionary<ILogger, SinkRegistration>(SinkReferenceComparer.Instance);
        private readonly HashSet<ILogger> _disposingSinks = new HashSet<ILogger>(SinkReferenceComparer.Instance);
        private readonly object _dispatchStateLock = new object();
        private readonly object _filterMutationLock = new object();
        private readonly object _shutdownLock = new object();
        private readonly object _sinkDisposalQueueLock = new object();
        private readonly ILogger[] _sinkDisposalQueue = new ILogger[MaxOwnedSinks];
        private readonly ILogger[] _pendingSinkDisposals = new ILogger[MaxOwnedSinks];
        private readonly ILogProcessor _processor;
        private readonly Func<DateTime> _instanceTimestampProvider;
        private readonly LoggerProcessingOptions _processingOptions;

        private volatile SinkRegistration[] _sinkSnapshot = Array.Empty<SinkRegistration>();
        private volatile HashSet<string> _whiteListSnapshot = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private volatile HashSet<string> _blackListSnapshot = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private volatile LogLevel _currentLogLevel = LogLevel.Info;
        private volatile LogFilter _currentLogFilter = LogFilter.LogAll;
#if !UNITY_WEBGL || UNITY_EDITOR
        private Thread _sinkDisposalThread;
#endif
        private bool _sinkDisposalWorkerOwnsQueue;
        private bool _sinkDisposalStopRequested;
        private int _sinkDisposalQueueHead;
        private int _sinkDisposalQueueCount;
        private int _pendingSinkDisposalCount;
        private int _activeDispatchCount;
        private int _sinkDisposalsOutstanding;
        private int _ownedSinkCount;
        private int _activeSinkCount;
        private int _lifecycleState;
        private LoggerShutdownResult _lastShutdownResult;
        private bool _shutdownProcessorStopped;
        private bool _shutdownFlushAttempted;
        private bool _shutdownSinksFlushed = true;
        private bool _shutdownSinksDetached;
        private long _shutdownDroppedMessageCount;
        private long _sinkFailureCount;
        private long _sinkDisposalFailureCount;
        private long _rejectedFilterMutationCount;
        private long _timestampProviderFailureCount;
        private long _messageBuilderFailureCount;
        private int _quarantinedSinkCount;
        private int _filterCategoryCount;
        private int _filterCharacters;
        private int _timestampProviderFailed;
        private int _messageBuilderFailureEmergencyReported;

        public static CLogger Instance
        {
            get
            {
                CLogger current = _instance;
                if (current != null)
                {
                    return current;
                }

                lock (InstanceLock)
                {
                    while (_shutdownInProgress)
                    {
                        Monitor.Wait(InstanceLock);
                    }

                    if (_globalCreationBlocked)
                    {
                        throw new InvalidOperationException("Global logger creation is blocked until the next runtime subsystem reset.");
                    }

                    _suppressGlobalStaticLogging = false;
                    return _instance ??= new CLogger(_processorFactory, _globalProcessingOptions, _timestampProvider);
                }
            }
        }

        public CLogger()
            : this(CreatePlatformDefaultProcessor, new LoggerProcessingOptions(), _timestampProvider)
        {
        }

        public CLogger(LoggerProcessingOptions processingOptions)
            : this(CreatePlatformDefaultProcessor, processingOptions, _timestampProvider)
        {
        }

        internal CLogger(
            Func<CLogger, LoggerProcessingOptions, ILogProcessor> processorFactory,
            LoggerProcessingOptions processingOptions,
            Func<DateTime> timestampProvider)
        {
            _processingOptions = LoggerProcessingOptions.CreateValidated(processingOptions);
            _instanceTimestampProvider = timestampProvider ?? (() => DateTime.UtcNow);
            LogMessagePool.Prewarm();
            StringBuilderPool.Prewarm();
            _processor = (processorFactory ?? throw new ArgumentNullException(nameof(processorFactory)))(this, _processingOptions);
        }

        internal static bool TryGetInstance(out CLogger instance)
        {
            instance = _instance;
            return instance != null;
        }

        internal static bool ConfigureProcessorFactory(
            Func<CLogger, LoggerProcessingOptions, ILogProcessor> factory,
            LoggerProcessingOptions processingOptions)
        {
            if (factory == null)
            {
                return false;
            }

            lock (InstanceLock)
            {
                if (_instance != null || _shutdownInProgress || _globalCreationBlocked)
                {
                    EmergencyLogger.TryWrite("Processing configuration was ignored because the global logger is already active.");
                    return false;
                }

                _processorFactory = factory;
                _globalProcessingOptions = LoggerProcessingOptions.CreateValidated(processingOptions);
                return true;
            }
        }

        public static bool ConfigureSingleThreadedProcessing(LoggerProcessingOptions options = null)
        {
            LoggerProcessingOptions capturedOptions = LoggerProcessingOptions.CreateValidated(options);
            return ConfigureProcessorFactory((owner, configured) => new SingleThreadLogProcessor(owner, configured), capturedOptions);
        }

        public static bool ConfigureThreadedProcessing(LoggerProcessingOptions options = null)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            throw new PlatformNotSupportedException("Threaded logger processing is unavailable in WebGL players. Use ConfigureSingleThreadedProcessing.");
#else
            LoggerProcessingOptions capturedOptions = LoggerProcessingOptions.CreateValidated(options);
            return ConfigureProcessorFactory((owner, configured) => new ThreadedLogProcessor(owner, configured), capturedOptions);
#endif
        }

        public static bool ConfigureTimestampProvider(Func<DateTime> timestampProvider)
        {
            if (timestampProvider == null)
            {
                throw new ArgumentNullException(nameof(timestampProvider));
            }

            lock (InstanceLock)
            {
                if (_instance != null || _shutdownInProgress || _globalCreationBlocked)
                {
                    EmergencyLogger.TryWrite("Timestamp provider configuration was ignored because the global logger is already active.");
                    return false;
                }

                _timestampProvider = timestampProvider;
                return true;
            }
        }

        public static LoggerShutdownResult Shutdown(LogFlushMode flushMode = LogFlushMode.Buffered)
        {
            CLogger instance;
            bool preserveSuppression;
            lock (InstanceLock)
            {
                while (_shutdownInProgress)
                {
                    Monitor.Wait(InstanceLock);
                }

                _shutdownInProgress = true;
                preserveSuppression = _suppressGlobalStaticLogging || _globalCreationBlocked;
                _suppressGlobalStaticLogging = true;
                instance = _instance;
                _instance = null;
            }

            LoggerShutdownResult result = default;
            try
            {
                result = instance == null
                    ? new LoggerShutdownResult(LoggerShutdownStatus.NotStarted, 0, true)
                    : instance.ShutdownInstance(flushMode);
            }
            finally
            {
                lock (InstanceLock)
                {
                    if (instance != null && !result.IsComplete && _instance == null)
                    {
                        _instance = instance;
                    }

                    _shutdownInProgress = false;
                    _suppressGlobalStaticLogging = preserveSuppression || _globalCreationBlocked;
                    Monitor.PulseAll(InstanceLock);
                }
            }

            return result;
        }

        internal static LoggerShutdownResult ResetForUnitySubsystemRegistration()
        {
            lock (InstanceLock)
            {
                _globalCreationBlocked = true;
                _suppressGlobalStaticLogging = true;
            }

            LoggerShutdownResult result = Shutdown(LogFlushMode.Buffered);
            if (!result.IsComplete && result.Status != LoggerShutdownStatus.NotStarted)
            {
                EmergencyLogger.TryWrite("Unity subsystem reset could not stop the previous logger within its timeout.");
                return result;
            }

            lock (InstanceLock)
            {
                _processorFactory = CreatePlatformDefaultProcessor;
                _globalProcessingOptions = new LoggerProcessingOptions();
                _timestampProvider = () => DateTime.UtcNow;
                _suppressGlobalStaticLogging = true;
            }

            LogMessagePool.Clear();
            StringBuilderPool.Clear();
            return result;
        }

        internal static void CompleteUnitySubsystemRegistrationReset(bool initializationAllowed)
        {
            lock (InstanceLock)
            {
                _globalCreationBlocked = !initializationAllowed;
                _suppressGlobalStaticLogging = true;
                Monitor.PulseAll(InstanceLock);
            }
        }

        internal static void ConfigureGlobalStaticLoggingSuppressed(bool suppress)
        {
            lock (InstanceLock)
            {
                if (_instance == null)
                {
                    _suppressGlobalStaticLogging = suppress;
                }
            }
        }

        internal static LoggerShutdownResult ShutdownForApplicationQuit()
        {
            lock (InstanceLock)
            {
                _globalCreationBlocked = true;
                _suppressGlobalStaticLogging = true;
            }

            return Shutdown(LogFlushMode.Buffered);
        }

        public void SetLogLevel(LogLevel level)
        {
            if (level < LogLevel.Trace || level > LogLevel.None)
            {
                throw new ArgumentOutOfRangeException(nameof(level));
            }

            _currentLogLevel = level;
        }

        public LogLevel GetLogLevel()
        {
            return _currentLogLevel;
        }

        public void SetLogFilter(LogFilter filter)
        {
            if (filter < LogFilter.LogAll || filter > LogFilter.LogNoBlackList)
            {
                throw new ArgumentOutOfRangeException(nameof(filter));
            }

            _currentLogFilter = filter;
        }

        public bool AddLogger(ILogger logger)
        {
            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            ThrowIfStopping();
            _sinksLock.EnterWriteLock();
            try
            {
                ThrowIfStopping();
                for (int i = 0; i < _sinks.Count; i++)
                {
                    if (ReferenceEquals(_sinks[i].Sink, logger))
                    {
                        return false;
                    }
                }

                if (_retiredRegistrations.ContainsKey(logger) || _disposingSinks.Contains(logger))
                {
                    return false;
                }

                if (!HasSinkOwnershipCapacityNoLock())
                {
                    return false;
                }

                var registration = new SinkRegistration(logger);
                SinkRegistration[] snapshot = CreateSnapshotWithAddedSinkNoLock(registration);
                _sinks.Add(registration);
                _sinkSnapshot = snapshot;
                Interlocked.Increment(ref _activeSinkCount);
                Interlocked.Increment(ref _ownedSinkCount);
                return true;
            }
            finally
            {
                _sinksLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Adds a sink when no sink of the same exact type is registered. A rejected new
        /// instance is disposed so callers can safely use AddLoggerUnique(new Sink()).
        /// </summary>
        public bool AddLoggerUnique(ILogger logger)
        {
            if (logger == null)
            {
                throw new ArgumentNullException(nameof(logger));
            }

            ThrowIfStopping();
            bool disposeRejected = false;
            _sinksLock.EnterWriteLock();
            try
            {
                ThrowIfStopping();
                Type type = logger.GetType();
                for (int i = 0; i < _sinks.Count; i++)
                {
                    if (!_sinks[i].IsRetired && _sinks[i].Sink.GetType() == type)
                    {
                        disposeRejected = !ReferenceEquals(_sinks[i].Sink, logger);
                        return false;
                    }
                }

                if (_retiredRegistrations.ContainsKey(logger) || _disposingSinks.Contains(logger))
                {
                    return false;
                }

                if (!HasSinkOwnershipCapacityNoLock())
                {
                    disposeRejected = true;
                    return false;
                }

                var registration = new SinkRegistration(logger);
                SinkRegistration[] snapshot = CreateSnapshotWithAddedSinkNoLock(registration);
                _sinks.Add(registration);
                _sinkSnapshot = snapshot;
                Interlocked.Increment(ref _activeSinkCount);
                Interlocked.Increment(ref _ownedSinkCount);
                return true;
            }
            finally
            {
                _sinksLock.ExitWriteLock();
                if (disposeRejected)
                {
                    TryDisposeSink(logger);
                }
            }
        }

        /// <summary>
        /// Removes a sink from future dispatch. A true result means all earlier dispatches
        /// have quiesced and sink ownership has transferred back to the caller. A false
        /// result means the caller must not dispose the sink because it was not registered,
        /// another owner already claimed it, or dispatches have not quiesced yet.
        /// </summary>
        public bool RemoveLogger(ILogger logger, int quiescenceTimeoutMs = DefaultSinkQuiescenceTimeoutMs)
        {
            if (logger == null)
            {
                return true;
            }

            SinkRegistration removed = null;
            _sinksLock.EnterWriteLock();
            try
            {
                for (int i = 0; i < _sinks.Count; i++)
                {
                    if (!ReferenceEquals(_sinks[i].Sink, logger))
                    {
                        continue;
                    }

                    removed = _sinks[i];
                    SinkRegistration[] snapshot = CreateSnapshotWithoutSinkNoLock(i);
                    _retiredRegistrations.Add(logger, removed);
                    if (!removed.Retire())
                    {
                        _retiredRegistrations.Remove(logger);
                        removed = null;
                        break;
                    }

                    if (Interlocked.Exchange(ref removed.ActiveCountReleased, 1) == 0)
                    {
                        Interlocked.Decrement(ref _activeSinkCount);
                    }

                    _sinks.RemoveAt(i);
                    _sinkSnapshot = snapshot;
                    break;
                }

                if (removed == null)
                {
                    _retiredRegistrations.TryGetValue(logger, out removed);
                }
            }
            finally
            {
                _sinksLock.ExitWriteLock();
            }

            if (removed == null)
            {
                return false;
            }

            if (!removed.WaitForQuiescence(quiescenceTimeoutMs))
            {
                return false;
            }

            _sinksLock.EnterWriteLock();
            bool ownershipTransferred = false;
            try
            {
                if (_retiredRegistrations.TryGetValue(logger, out SinkRegistration tracked)
                    && ReferenceEquals(tracked, removed))
                {
                    _retiredRegistrations.Remove(logger);
                    Interlocked.Decrement(ref _ownedSinkCount);
                    ownershipTransferred = true;
                }
            }
            finally
            {
                _sinksLock.ExitWriteLock();
            }

            return ownershipTransferred;
        }

        public void ClearLoggers()
        {
            lock (_shutdownLock)
            {
                ThrowIfStopping();
                DetachAllLoggers();
                WaitForActiveDispatches(DefaultSinkQuiescenceTimeoutMs);
            }
        }

        private void DetachAllLoggers()
        {
            List<ILogger> toDispose;
            _sinksLock.EnterWriteLock();
            try
            {
                toDispose = new List<ILogger>(_sinks.Count + _retiredRegistrations.Count);
                for (int i = 0; i < _sinks.Count; i++)
                {
                    toDispose.Add(_sinks[i].Sink);
                }

                foreach (KeyValuePair<ILogger, SinkRegistration> pair in _retiredRegistrations)
                {
                    toDispose.Add(pair.Key);
                }

                int disposingAdded = 0;
                try
                {
                    for (int i = 0; i < toDispose.Count; i++)
                    {
                        if (!_disposingSinks.Add(toDispose[i]))
                        {
                            throw new InvalidOperationException("A sink cannot enter disposal ownership more than once.");
                        }

                        disposingAdded++;
                    }
                }
                catch
                {
                    for (int i = 0; i < disposingAdded; i++)
                    {
                        _disposingSinks.Remove(toDispose[i]);
                    }

                    throw;
                }

                for (int i = 0; i < _sinks.Count; i++)
                {
                    _sinks[i].Retire();
                    Interlocked.Exchange(ref _sinks[i].ActiveCountReleased, 1);
                }

                foreach (KeyValuePair<ILogger, SinkRegistration> pair in _retiredRegistrations)
                {
                    pair.Value.Retire();
                }

                _sinks.Clear();
                _retiredRegistrations.Clear();
                _sinkSnapshot = Array.Empty<SinkRegistration>();
                Volatile.Write(ref _activeSinkCount, 0);
            }
            finally
            {
                _sinksLock.ExitWriteLock();
            }

            ScheduleSinkDisposals(toDispose);
        }

        public void AddToWhiteList(string category)
        {
            MutateCategorySet(category, true, true);
        }

        public void RemoveFromWhiteList(string category)
        {
            MutateCategorySet(category, true, false);
        }

        public void AddToBlackList(string category)
        {
            MutateCategorySet(category, false, true);
        }

        public void RemoveFromBlackList(string category)
        {
            MutateCategorySet(category, false, false);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Log(
            LogLevel level,
            string message,
            string category = null,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
        {
            EnqueueMessage(level, message, category, filePath, lineNumber, memberName);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Log(
            LogLevel level,
            Action<StringBuilder> messageBuilder,
            string category = null,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
        {
            EnqueueMessage(level, messageBuilder, category, filePath, lineNumber, memberName);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Log<T>(
            LogLevel level,
            T state,
            Action<T, StringBuilder> messageBuilder,
            string category = null,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
        {
            EnqueueMessage(level, state, messageBuilder, category, filePath, lineNumber, memberName);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void LogGlobal(
            LogLevel level,
            string message,
            string category = null,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
        {
            if (TryGetGlobalInstanceForLogging(out CLogger logger))
            {
                logger.EnqueueMessage(level, message, category, filePath, lineNumber, memberName);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void LogGlobal(
            LogLevel level,
            Action<StringBuilder> messageBuilder,
            string category = null,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
        {
            if (TryGetGlobalInstanceForLogging(out CLogger logger))
            {
                logger.EnqueueMessage(level, messageBuilder, category, filePath, lineNumber, memberName);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void LogGlobal<T>(
            LogLevel level,
            T state,
            Action<T, StringBuilder> messageBuilder,
            string category = null,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
        {
            if (TryGetGlobalInstanceForLogging(out CLogger logger))
            {
                logger.EnqueueMessage(level, state, messageBuilder, category, filePath, lineNumber, memberName);
            }
        }

        public static void LogTrace(string message, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "") => LogGlobal(LogLevel.Trace, message, category, filePath, lineNumber, memberName);
        public static void LogDebug(string message, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "") => LogGlobal(LogLevel.Debug, message, category, filePath, lineNumber, memberName);
        public static void LogInfo(string message, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "") => LogGlobal(LogLevel.Info, message, category, filePath, lineNumber, memberName);
        public static void LogWarning(string message, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "") => LogGlobal(LogLevel.Warning, message, category, filePath, lineNumber, memberName);
        public static void LogError(string message, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "") => LogGlobal(LogLevel.Error, message, category, filePath, lineNumber, memberName);
        public static void LogFatal(string message, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "") => LogGlobal(LogLevel.Fatal, message, category, filePath, lineNumber, memberName);

        public static void LogTrace(Action<StringBuilder> messageBuilder, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "") => LogGlobal(LogLevel.Trace, messageBuilder, category, filePath, lineNumber, memberName);
        public static void LogDebug(Action<StringBuilder> messageBuilder, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "") => LogGlobal(LogLevel.Debug, messageBuilder, category, filePath, lineNumber, memberName);
        public static void LogInfo(Action<StringBuilder> messageBuilder, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "") => LogGlobal(LogLevel.Info, messageBuilder, category, filePath, lineNumber, memberName);
        public static void LogWarning(Action<StringBuilder> messageBuilder, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "") => LogGlobal(LogLevel.Warning, messageBuilder, category, filePath, lineNumber, memberName);
        public static void LogError(Action<StringBuilder> messageBuilder, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "") => LogGlobal(LogLevel.Error, messageBuilder, category, filePath, lineNumber, memberName);
        public static void LogFatal(Action<StringBuilder> messageBuilder, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "") => LogGlobal(LogLevel.Fatal, messageBuilder, category, filePath, lineNumber, memberName);

        public static void LogTrace<T>(T state, Action<T, StringBuilder> messageBuilder, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "") => LogGlobal(LogLevel.Trace, state, messageBuilder, category, filePath, lineNumber, memberName);
        public static void LogDebug<T>(T state, Action<T, StringBuilder> messageBuilder, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "") => LogGlobal(LogLevel.Debug, state, messageBuilder, category, filePath, lineNumber, memberName);
        public static void LogInfo<T>(T state, Action<T, StringBuilder> messageBuilder, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "") => LogGlobal(LogLevel.Info, state, messageBuilder, category, filePath, lineNumber, memberName);
        public static void LogWarning<T>(T state, Action<T, StringBuilder> messageBuilder, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "") => LogGlobal(LogLevel.Warning, state, messageBuilder, category, filePath, lineNumber, memberName);
        public static void LogError<T>(T state, Action<T, StringBuilder> messageBuilder, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "") => LogGlobal(LogLevel.Error, state, messageBuilder, category, filePath, lineNumber, memberName);
        public static void LogFatal<T>(T state, Action<T, StringBuilder> messageBuilder, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "") => LogGlobal(LogLevel.Fatal, state, messageBuilder, category, filePath, lineNumber, memberName);

        internal void EnqueueMessage(LogLevel level, string message, string category, string filePath, int lineNumber, string memberName)
        {
            if (!CanAccept(level, category))
            {
                return;
            }

            int estimate = EstimateRetainedCharacters(message?.Length ?? 0, category, filePath, memberName);
            if (!_processor.TryReserve(level, estimate, true, out int reservedCharacters))
            {
                return;
            }

            LogMessage entry = null;
            bool reservationOwned = true;
            try
            {
                entry = LogMessagePool.Get();
                entry.Initialize(
                    GetTimestampSafely(),
                    level,
                    message,
                    null,
                    category,
                    filePath,
                    lineNumber,
                    memberName,
                    _processingOptions.MaxMessageCharacters,
                    _processingOptions.MaxCategoryCharacters,
                    _processingOptions.MaxSourcePathCharacters,
                    _processingOptions.MaxMemberNameCharacters);
                reservationOwned = false;
                if (_processor.TryCommit(entry, reservedCharacters, entry.GetRetainedCharacterCount()))
                {
                    entry = null;
                }
            }
            catch
            {
                if (reservationOwned)
                {
                    _processor.CancelReservation(reservedCharacters);
                }

                throw;
            }
            finally
            {
                if (entry != null)
                {
                    LogMessagePool.Return(entry);
                }
            }
        }

        internal void EnqueueMessage(LogLevel level, Action<StringBuilder> messageBuilder, string category, string filePath, int lineNumber, string memberName)
        {
            EnqueueMessage(level, messageBuilder, InvokeMessageBuilder, category, filePath, lineNumber, memberName);
        }

        internal void EnqueueMessage<T>(LogLevel level, T state, Action<T, StringBuilder> messageBuilder, string category, string filePath, int lineNumber, string memberName)
        {
            if (!CanAccept(level, category))
            {
                return;
            }

            // Reserve the largest queue-owned payload before invoking user code. The callback
            // itself remains caller-controlled, but concurrent logger-owned builders cannot
            // oversubscribe the configured retained queue budget.
            int estimate = EstimateRetainedCharacters(_processingOptions.MaxMessageCharacters, category, filePath, memberName);
            if (!_processor.TryReserve(level, estimate, false, out int reservedCharacters))
            {
                return;
            }

            StringBuilder builder = null;
            string boundedMessage = null;
            bool builderTruncated = false;
            LogMessage entry = null;
            bool reservationOwned = true;
            try
            {
                DateTime timestamp = GetTimestampSafely();
                builder = StringBuilderPool.Get();
                try
                {
                    messageBuilder?.Invoke(state, builder);
                }
                catch (Exception exception) when (!(exception is OutOfMemoryException))
                {
                    Interlocked.Increment(ref _messageBuilderFailureCount);
                    builder.Clear();
                    builder.Append("[log message builder failed: ");
                    builder.Append(exception.GetType().Name);
                    builder.Append(']');
                    if (Interlocked.CompareExchange(ref _messageBuilderFailureEmergencyReported, 1, 0) == 0)
                    {
                        EmergencyLogger.TryWrite(
                            "A log message builder callback failed; bounded diagnostic entries will be emitted and further emergency reports are suppressed.",
                            exception);
                    }
                }

                DetachOversizedBuilder(ref builder, out boundedMessage, out builderTruncated);
                entry = LogMessagePool.Get();
                entry.Initialize(
                    timestamp,
                    level,
                    boundedMessage,
                    builder,
                    category,
                    filePath,
                    lineNumber,
                    memberName,
                    _processingOptions.MaxMessageCharacters,
                    _processingOptions.MaxCategoryCharacters,
                    _processingOptions.MaxSourcePathCharacters,
                    _processingOptions.MaxMemberNameCharacters,
                    builderTruncated);
                builder = null;
                reservationOwned = false;
                if (_processor.TryCommit(entry, reservedCharacters, entry.GetRetainedCharacterCount()))
                {
                    entry = null;
                }
            }
            finally
            {
                if (reservationOwned)
                {
                    _processor.CancelReservation(reservedCharacters);
                }

                if (entry != null)
                {
                    LogMessagePool.Return(entry);
                }
                else if (builder != null)
                {
                    StringBuilderPool.Return(builder);
                }
            }
        }

        internal void DispatchToLoggers(LogMessage message)
        {
            BeginDispatch();
            SinkRegistration[] snapshot = _sinkSnapshot;
            try
            {
                for (int i = 0; i < snapshot.Length; i++)
                {
                    SinkRegistration registration = snapshot[i];
                    if (!registration.TryEnter())
                    {
                        continue;
                    }

                    try
                    {
                        try
                        {
                            registration.Sink.Log(message);
                            Volatile.Write(ref registration.ConsecutiveFailures, 0);
                        }
                        catch (Exception exception)
                        {
                            RecordSinkFailure(registration, exception);
                        }
                    }
                    finally
                    {
                        registration.Exit();
                    }
                }
            }
            finally
            {
                EndDispatch();
            }
        }

        internal void PerformSinkMaintenance()
        {
            BeginDispatch();
            SinkRegistration[] snapshot = _sinkSnapshot;
            try
            {
                for (int i = 0; i < snapshot.Length; i++)
                {
                    SinkRegistration registration = snapshot[i];
                    if (!(registration.Sink is IMaintainableLogger maintainable)
                        || !registration.TryEnter())
                    {
                        continue;
                    }

                    try
                    {
                        try
                        {
                            maintainable.PerformMaintenance();
                        }
                        catch (Exception exception)
                        {
                            RecordSinkFailure(registration, exception);
                        }
                    }
                    finally
                    {
                        registration.Exit();
                    }
                }
            }
            finally
            {
                EndDispatch();
            }
        }

        public void Pump(int maxItems = 256)
        {
            _processor.Pump(maxItems, -1);
        }

        internal void PumpWithinBudget(int maxItems, int budgetMilliseconds)
        {
            _processor.Pump(maxItems, Math.Max(budgetMilliseconds, 0));
        }

        public bool TryFlush(LogFlushMode mode = LogFlushMode.Buffered, int timeoutMs = -1)
        {
            if (timeoutMs < 0)
            {
                timeoutMs = _processingOptions.ShutdownDrainTimeoutMs;
            }

            long startTimestamp = Stopwatch.GetTimestamp();
            if (!_processor.TryFlush(GetRemainingTimeout(startTimestamp, timeoutMs))
                || !WaitForActiveDispatches(GetRemainingTimeout(startTimestamp, timeoutMs)))
            {
                return false;
            }

            return FlushSinks(mode);
        }

        public LogProcessingStatistics GetProcessingStatistics()
        {
            return _processor.GetStatistics().WithSinkStatistics(
                Interlocked.Read(ref _sinkFailureCount),
                Interlocked.Read(ref _sinkDisposalFailureCount),
                Volatile.Read(ref _sinkDisposalsOutstanding),
                Volatile.Read(ref _quarantinedSinkCount),
                Volatile.Read(ref _filterCategoryCount),
                Volatile.Read(ref _filterCharacters),
                Interlocked.Read(ref _rejectedFilterMutationCount),
                Interlocked.Read(ref _timestampProviderFailureCount),
                Interlocked.Read(ref _messageBuilderFailureCount));
        }

        internal int MessageBuilderFailureEmergencyReportCount =>
            Volatile.Read(ref _messageBuilderFailureEmergencyReported);

        internal bool IsSinkDisposalExecutorRunning
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return false;
#else
                lock (_sinkDisposalQueueLock)
                {
                    return _sinkDisposalWorkerOwnsQueue;
                }
#endif
            }
        }

#if UNITY_INCLUDE_TESTS
        internal Action SinkDisposalBeforeExitTestHook;
#endif

        public static LoggerMemoryStatistics GetMemoryStatistics()
        {
            LogMessagePool.PoolStatistics messages = LogMessagePool.GetStatistics();
            StringBuilderPool.PoolStatistics builders = StringBuilderPool.GetStatistics();
            return new LoggerMemoryStatistics(
                messages.CurrentSize,
                messages.PeakSize,
                messages.TotalMisses,
                messages.TotalDiscards,
                messages.InvalidReturns,
                builders.CurrentSize,
                builders.PeakSize,
                builders.TotalMisses,
                builders.TotalDiscards);
        }

        public LoggerShutdownResult ShutdownInstance(LogFlushMode flushMode = LogFlushMode.Buffered, int timeoutMs = -1)
        {
            lock (InstanceLock)
            {
                if (ReferenceEquals(_instance, this) && !_shutdownInProgress)
                {
                    throw new InvalidOperationException("Use static CLogger.Shutdown when stopping the global logger instance.");
                }
            }

            lock (_shutdownLock)
            {
                if (timeoutMs < 0)
                {
                    timeoutMs = _processingOptions.ShutdownDrainTimeoutMs;
                }

                long startTimestamp = Stopwatch.GetTimestamp();

                int state = Interlocked.CompareExchange(ref _lifecycleState, 1, 0);
                if (state == 2)
                {
                    return _lastShutdownResult;
                }

                if (!_shutdownProcessorStopped)
                {
                    LoggerShutdownResult processorResult = _processor.Shutdown(
                        GetRemainingTimeout(startTimestamp, timeoutMs));
                    _shutdownDroppedMessageCount = Math.Max(
                        _shutdownDroppedMessageCount,
                        processorResult.DroppedMessageCount);
                    if (!processorResult.IsComplete || !_processor.IsStopped)
                    {
                        return new LoggerShutdownResult(
                            LoggerShutdownStatus.TimedOut,
                            _shutdownDroppedMessageCount,
                            _shutdownFlushAttempted && _shutdownSinksFlushed);
                    }

                    _shutdownProcessorStopped = true;
                }

                if (!_shutdownFlushAttempted)
                {
                    _shutdownSinksFlushed = FlushSinks(flushMode);
                    _shutdownFlushAttempted = true;
                }

                if (!_shutdownSinksDetached)
                {
                    DetachAllLoggers();
                    _shutdownSinksDetached = true;
                }

                bool dispatchesCompleted = WaitForActiveDispatches(
                    GetRemainingTimeout(startTimestamp, timeoutMs));
                bool disposalExecutorStopped = StopSinkDisposalExecutor(
                    dispatchesCompleted ? GetRemainingTimeout(startTimestamp, timeoutMs) : 0);
                if (!dispatchesCompleted || !disposalExecutorStopped)
                {
                    return new LoggerShutdownResult(
                        LoggerShutdownStatus.TimedOut,
                        _shutdownDroppedMessageCount,
                        _shutdownSinksFlushed);
                }

                _processor.Dispose();
                bool hasFailures = !_shutdownSinksFlushed
                    || Interlocked.Read(ref _sinkDisposalFailureCount) != 0;
                LoggerShutdownStatus status = hasFailures
                    ? LoggerShutdownStatus.CompletedWithFailures
                    : _shutdownDroppedMessageCount > 0
                        ? LoggerShutdownStatus.CompletedWithDrops
                        : LoggerShutdownStatus.Completed;
                _lastShutdownResult = new LoggerShutdownResult(
                    status,
                    _shutdownDroppedMessageCount,
                    _shutdownSinksFlushed);
                Volatile.Write(ref _lifecycleState, 2);
                return _lastShutdownResult;
            }
        }

        public void Dispose()
        {
            bool detachedGlobal = false;
            lock (InstanceLock)
            {
                while (_shutdownInProgress)
                {
                    Monitor.Wait(InstanceLock);
                }

                if (ReferenceEquals(_instance, this))
                {
                    _shutdownInProgress = true;
                    _suppressGlobalStaticLogging = true;
                    _instance = null;
                    detachedGlobal = true;
                }
            }

            if (!detachedGlobal)
            {
                LoggerShutdownResult explicitResult = ShutdownInstance();
                if (!explicitResult.IsComplete)
                {
                    EmergencyLogger.TryWrite("CLogger.Dispose timed out. Keep the instance and retry ShutdownInstance after releasing blocked sinks.");
                }

                return;
            }

            LoggerShutdownResult result = default;
            try
            {
                result = ShutdownInstance();
            }
            finally
            {
                lock (InstanceLock)
                {
                    if (!result.IsComplete && _instance == null)
                    {
                        _instance = this;
                    }

                    _shutdownInProgress = false;
                    _suppressGlobalStaticLogging = false;
                    Monitor.PulseAll(InstanceLock);
                }
            }

            if (!result.IsComplete)
            {
                EmergencyLogger.TryWrite("Global CLogger.Dispose timed out; the global instance was restored for an explicit shutdown retry.");
            }
        }

        private static void InvokeMessageBuilder(Action<StringBuilder> append, StringBuilder builder)
        {
            append?.Invoke(builder);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool CanAccept(LogLevel level, string category)
        {
            return Volatile.Read(ref _lifecycleState) == 0
                && Volatile.Read(ref _activeSinkCount) > 0
                && ShouldLog(level, category);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ShouldLog(LogLevel level, string category)
        {
            if (level < LogLevel.Trace || level >= LogLevel.None || level < _currentLogLevel)
            {
                return false;
            }

            LogFilter filter = _currentLogFilter;
            if (filter == LogFilter.LogAll)
            {
                return true;
            }

            if (!string.IsNullOrEmpty(category)
                && category.Length > _processingOptions.MaxCategoryCharacters)
            {
                return false;
            }

            if (filter == LogFilter.LogWhiteList)
            {
                return !string.IsNullOrEmpty(category) && _whiteListSnapshot.Contains(category);
            }

            return string.IsNullOrEmpty(category) || !_blackListSnapshot.Contains(category);
        }

        private static bool TryGetGlobalInstanceForLogging(out CLogger logger)
        {
            logger = _instance;
            if (logger != null)
            {
                return true;
            }

            if (_suppressGlobalStaticLogging || _shutdownInProgress || _globalCreationBlocked)
            {
                return false;
            }

            lock (InstanceLock)
            {
                if (_suppressGlobalStaticLogging || _shutdownInProgress || _globalCreationBlocked)
                {
                    logger = null;
                    return false;
                }

                logger = _instance ??= new CLogger(_processorFactory, _globalProcessingOptions, _timestampProvider);
                return true;
            }
        }

        private static ILogProcessor CreatePlatformDefaultProcessor(CLogger owner, LoggerProcessingOptions options)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return new SingleThreadLogProcessor(owner, options);
#else
            return new ThreadedLogProcessor(owner, options);
#endif
        }

        private void MutateCategorySet(string category, bool whiteList, bool add)
        {
            if (string.IsNullOrEmpty(category))
            {
                return;
            }

            lock (_filterMutationLock)
            {
                HashSet<string> current = whiteList ? _whiteListSnapshot : _blackListSnapshot;
                if (current.TryGetValue(category, out string storedCategory))
                {
                    if (add)
                    {
                        return;
                    }

                    var reduced = new HashSet<string>(current, StringComparer.OrdinalIgnoreCase);
                    reduced.Remove(category);
                    _filterCategoryCount--;
                    _filterCharacters -= storedCategory.Length;
                    if (whiteList)
                    {
                        _whiteListSnapshot = reduced;
                    }
                    else
                    {
                        _blackListSnapshot = reduced;
                    }

                    return;
                }

                if (!add)
                {
                    return;
                }

                if (category.Length > _processingOptions.MaxCategoryCharacters)
                {
                    Interlocked.Increment(ref _rejectedFilterMutationCount);
                    throw new ArgumentOutOfRangeException(
                        nameof(category),
                        "Filter categories cannot exceed the configured MaxCategoryCharacters limit.");
                }

                if (_filterCategoryCount >= _processingOptions.MaxFilterCategories
                    || (long)_filterCharacters + category.Length > _processingOptions.MaxFilterCharacters)
                {
                    Interlocked.Increment(ref _rejectedFilterMutationCount);
                    throw new InvalidOperationException("The configured logger category-filter memory budget was exhausted.");
                }

                var updated = new HashSet<string>(current, StringComparer.OrdinalIgnoreCase);
                updated.Add(category);
                _filterCategoryCount++;
                _filterCharacters += category.Length;
                if (whiteList)
                {
                    _whiteListSnapshot = updated;
                }
                else
                {
                    _blackListSnapshot = updated;
                }
            }
        }

        private SinkRegistration[] CreateSnapshotWithAddedSinkNoLock(SinkRegistration registration)
        {
            var snapshot = new SinkRegistration[_sinks.Count + 1];
            _sinks.CopyTo(snapshot, 0);
            snapshot[snapshot.Length - 1] = registration;
            return snapshot;
        }

        private SinkRegistration[] CreateSnapshotWithoutSinkNoLock(int removedIndex)
        {
            if (_sinks.Count == 1)
            {
                return Array.Empty<SinkRegistration>();
            }

            var snapshot = new SinkRegistration[_sinks.Count - 1];
            int destination = 0;
            for (int i = 0; i < _sinks.Count; i++)
            {
                if (i != removedIndex)
                {
                    snapshot[destination++] = _sinks[i];
                }
            }

            return snapshot;
        }

        private bool HasSinkOwnershipCapacityNoLock()
        {
            return Volatile.Read(ref _ownedSinkCount) < MaxOwnedSinks;
        }

        private void BeginDispatch()
        {
            Interlocked.Increment(ref _activeDispatchCount);
        }

        private void EndDispatch()
        {
            if (Interlocked.Decrement(ref _activeDispatchCount) != 0)
            {
                return;
            }

            bool useSynchronousFallback = false;
            lock (_dispatchStateLock)
            {
                if (_pendingSinkDisposalCount > 0)
                {
                    lock (_sinkDisposalQueueLock)
                    {
                        for (int i = 0; i < _pendingSinkDisposalCount; i++)
                        {
                            EnqueueSinkDisposalNoLock(_pendingSinkDisposals[i]);
                            _pendingSinkDisposals[i] = null;
                        }

                        _pendingSinkDisposalCount = 0;
                        if (_sinkDisposalStopRequested)
                        {
                            useSynchronousFallback = !HasSinkDisposalWorkerNoLock();
                        }
                        else
                        {
                            useSynchronousFallback = !TryEnsureSinkDisposalThreadNoLock();
                        }

                        Monitor.PulseAll(_sinkDisposalQueueLock);
                    }
                }
                else
                {
                    Monitor.PulseAll(_dispatchStateLock);
                }
            }

            if (useSynchronousFallback)
            {
                DrainSinkDisposalQueueSynchronously();
            }
        }

        private bool WaitForActiveDispatches(int timeoutMs)
        {
            int startTick = Environment.TickCount;
            lock (_dispatchStateLock)
            {
                while (Volatile.Read(ref _activeDispatchCount) != 0 || _sinkDisposalsOutstanding != 0)
                {
                    int remaining = timeoutMs < 0
                        ? Timeout.Infinite
                        : timeoutMs - unchecked(Environment.TickCount - startTick);
                    if (remaining <= 0)
                    {
                        return false;
                    }

                    Monitor.Wait(_dispatchStateLock, remaining);
                }

                return true;
            }
        }

        private void RecordSinkFailure(SinkRegistration registration, Exception exception)
        {
            Interlocked.Increment(ref _sinkFailureCount);
            int failures = Interlocked.Increment(ref registration.ConsecutiveFailures);
            if (failures < _processingOptions.SinkFailureThreshold)
            {
                return;
            }

            var disposalBatch = new List<ILogger>(1) { registration.Sink };

            _sinksLock.EnterWriteLock();
            bool removed = false;
            try
            {
                int registrationIndex = _sinks.IndexOf(registration);
                if (registrationIndex >= 0
                    && Volatile.Read(ref registration.QuarantinedByFailure) == 0)
                {
                    SinkRegistration[] snapshot = CreateSnapshotWithoutSinkNoLock(registrationIndex);
                    if (!_disposingSinks.Add(registration.Sink))
                    {
                        return;
                    }

                    if (!registration.Retire()
                        || Interlocked.CompareExchange(ref registration.QuarantinedByFailure, 1, 0) != 0)
                    {
                        _disposingSinks.Remove(registration.Sink);
                        return;
                    }

                    _sinks.RemoveAt(registrationIndex);
                    _sinkSnapshot = snapshot;
                    Interlocked.Increment(ref _quarantinedSinkCount);
                    if (Interlocked.Exchange(ref registration.ActiveCountReleased, 1) == 0)
                    {
                        Interlocked.Decrement(ref _activeSinkCount);
                    }

                    removed = true;
                }
            }
            finally
            {
                _sinksLock.ExitWriteLock();
            }

            if (!removed)
            {
                return;
            }

            ScheduleSinkDisposals(disposalBatch);

            EmergencyLogger.TryWrite(
                "A failing log sink was quarantined: " + registration.Sink.GetType().FullName
                + " (" + exception.GetType().Name + ").");
        }

        private bool FlushSinks(LogFlushMode mode)
        {
            bool success = true;
            BeginDispatch();
            SinkRegistration[] snapshot = _sinkSnapshot;
            try
            {
                for (int i = 0; i < snapshot.Length; i++)
                {
                    SinkRegistration registration = snapshot[i];
                    if (!(registration.Sink is IFlushableLogger flushable)
                        || !registration.TryEnter())
                    {
                        continue;
                    }

                    try
                    {
                        try
                        {
                            success &= flushable.TryFlush(mode);
                        }
                        catch (Exception exception)
                        {
                            success = false;
                            RecordSinkFailure(registration, exception);
                        }
                    }
                    finally
                    {
                        registration.Exit();
                    }
                }
            }
            finally
            {
                EndDispatch();
            }

            return success;
        }

        private void ScheduleSinkDisposals(List<ILogger> sinks)
        {
            if (sinks == null || sinks.Count == 0)
            {
                return;
            }

            bool disposeImmediately;
            lock (_dispatchStateLock)
            {
                if (_sinkDisposalsOutstanding > MaxOwnedSinks - sinks.Count)
                {
                    throw new InvalidOperationException("Sink disposal ownership capacity was exceeded.");
                }

                _sinkDisposalsOutstanding += sinks.Count;
                disposeImmediately = Volatile.Read(ref _activeDispatchCount) == 0;
                if (!disposeImmediately)
                {
                    if (_pendingSinkDisposalCount > _pendingSinkDisposals.Length - sinks.Count)
                    {
                        _sinkDisposalsOutstanding -= sinks.Count;
                        throw new InvalidOperationException("Pending sink disposal capacity was exceeded.");
                    }

                    for (int i = 0; i < sinks.Count; i++)
                    {
                        _pendingSinkDisposals[_pendingSinkDisposalCount++] = sinks[i];
                    }
                }
            }

            if (!disposeImmediately)
            {
                return;
            }

            QueueSinkDisposals(sinks);
        }

        private void CompleteSinkDisposal(ILogger sink)
        {
            _sinksLock.EnterWriteLock();
            try
            {
                _disposingSinks.Remove(sink);
            }
            finally
            {
                _sinksLock.ExitWriteLock();
            }

            lock (_dispatchStateLock)
            {
                _sinkDisposalsOutstanding--;
                if (_sinkDisposalsOutstanding < 0)
                {
                    _sinkDisposalsOutstanding = 0;
                }

                int ownedCount = Interlocked.Decrement(ref _ownedSinkCount);
                if (ownedCount < 0)
                {
                    Interlocked.Exchange(ref _ownedSinkCount, 0);
                }

                Monitor.PulseAll(_dispatchStateLock);
            }
        }

        private void QueueSinkDisposals(List<ILogger> sinks)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            for (int i = 0; i < sinks.Count; i++)
            {
                TryDisposeSink(sinks[i]);
                CompleteSinkDisposal(sinks[i]);
            }
#else
            bool useSynchronousFallback;
            lock (_sinkDisposalQueueLock)
            {
                for (int i = 0; i < sinks.Count; i++)
                {
                    EnqueueSinkDisposalNoLock(sinks[i]);
                }

                if (_sinkDisposalStopRequested)
                {
                    useSynchronousFallback = !HasSinkDisposalWorkerNoLock();
                }
                else
                {
                    useSynchronousFallback = !TryEnsureSinkDisposalThreadNoLock();
                }

                Monitor.PulseAll(_sinkDisposalQueueLock);
            }

            if (useSynchronousFallback)
            {
                DrainSinkDisposalQueueSynchronously();
            }
#endif
        }

#if !UNITY_WEBGL || UNITY_EDITOR
        private void ProcessSinkDisposals()
        {
            try
            {
                while (true)
                {
                    ILogger sink;
                    lock (_sinkDisposalQueueLock)
                    {
                        while (_sinkDisposalQueueCount == 0 && !_sinkDisposalStopRequested)
                        {
                            Monitor.Wait(_sinkDisposalQueueLock);
                        }

                        if (_sinkDisposalQueueCount == 0)
                        {
#if UNITY_INCLUDE_TESTS
                            SinkDisposalBeforeExitTestHook?.Invoke();
#endif
                            ReleaseSinkDisposalWorkerOwnershipNoLock();
                            return;
                        }

                        sink = DequeueSinkDisposalNoLock();
                    }

                    TryDisposeSink(sink);
                    CompleteSinkDisposal(sink);
                }
            }
            finally
            {
                lock (_sinkDisposalQueueLock)
                {
                    if (ReferenceEquals(_sinkDisposalThread, Thread.CurrentThread))
                    {
                        ReleaseSinkDisposalWorkerOwnershipNoLock();
                    }
                }
            }
        }
#endif

        private void EnqueueSinkDisposalNoLock(ILogger sink)
        {
            if (_sinkDisposalQueueCount >= _sinkDisposalQueue.Length)
            {
                throw new InvalidOperationException("Sink disposal queue capacity was exceeded.");
            }

            int tail = (_sinkDisposalQueueHead + _sinkDisposalQueueCount) % _sinkDisposalQueue.Length;
            _sinkDisposalQueue[tail] = sink;
            _sinkDisposalQueueCount++;
        }

        private ILogger DequeueSinkDisposalNoLock()
        {
            ILogger sink = _sinkDisposalQueue[_sinkDisposalQueueHead];
            _sinkDisposalQueue[_sinkDisposalQueueHead] = null;
            _sinkDisposalQueueHead = (_sinkDisposalQueueHead + 1) % _sinkDisposalQueue.Length;
            _sinkDisposalQueueCount--;
            return sink;
        }

        private bool TryEnsureSinkDisposalThreadNoLock()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return false;
#else
            if (_sinkDisposalWorkerOwnsQueue)
            {
                return true;
            }

            try
            {
                _sinkDisposalThread = new Thread(ProcessSinkDisposals)
                {
                    Name = "CLogger.SinkDisposal",
                    IsBackground = true
                };
                _sinkDisposalWorkerOwnsQueue = true;
                _sinkDisposalThread.Start();
                return true;
            }
            catch (Exception exception)
            {
                _sinkDisposalWorkerOwnsQueue = false;
                _sinkDisposalThread = null;
                EmergencyLogger.TryWrite("Sink disposal executor was unavailable; disposal is running synchronously.", exception);
                return false;
            }
#endif
        }

        private bool HasSinkDisposalWorkerNoLock()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return false;
#else
            return _sinkDisposalWorkerOwnsQueue;
#endif
        }

#if !UNITY_WEBGL || UNITY_EDITOR
        private void ReleaseSinkDisposalWorkerOwnershipNoLock()
        {
            _sinkDisposalWorkerOwnsQueue = false;
            if (ReferenceEquals(_sinkDisposalThread, Thread.CurrentThread))
            {
                _sinkDisposalThread = null;
            }

            Monitor.PulseAll(_sinkDisposalQueueLock);
        }
#endif

        private void DrainSinkDisposalQueueSynchronously()
        {
            while (true)
            {
                ILogger sink;
                lock (_sinkDisposalQueueLock)
                {
                    if (_sinkDisposalQueueCount == 0)
                    {
                        return;
                    }

                    sink = DequeueSinkDisposalNoLock();
                }

                TryDisposeSink(sink);
                CompleteSinkDisposal(sink);
            }
        }
        private bool StopSinkDisposalExecutor(int timeoutMs)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return true;
#else
            Thread disposalThread;
            lock (_sinkDisposalQueueLock)
            {
                _sinkDisposalStopRequested = true;
                disposalThread = _sinkDisposalThread;
                Monitor.PulseAll(_sinkDisposalQueueLock);
            }

            if (disposalThread == null || !disposalThread.IsAlive)
            {
                return true;
            }

            if (ReferenceEquals(disposalThread, Thread.CurrentThread))
            {
                return false;
            }

            return disposalThread.Join(timeoutMs);
#endif
        }

        private void TryDisposeSink(ILogger sink)
        {
            Exception lastException = null;
            int attemptCount = sink is IIdempotentLoggerSinkDisposal ? SinkDisposeAttemptCount : 1;
            for (int attempt = 0; attempt < attemptCount; attempt++)
            {
                try
                {
                    sink.Dispose();
                    return;
                }
                catch (Exception exception)
                {
                    lastException = exception;
                }
            }

            Interlocked.Increment(ref _sinkDisposalFailureCount);
            EmergencyLogger.TryWrite("A log sink failed all bounded disposal attempts.", lastException);
        }

        private void ThrowIfStopping()
        {
            if (Volatile.Read(ref _lifecycleState) != 0)
            {
                throw new ObjectDisposedException(nameof(CLogger));
            }
        }

        private static int GetRemainingTimeout(long startTimestamp, int timeoutMs)
        {
            if (timeoutMs < 0)
            {
                return Timeout.Infinite;
            }

            long elapsedTicks = Stopwatch.GetTimestamp() - startTimestamp;
            long elapsedSeconds = elapsedTicks / Stopwatch.Frequency;
            if (elapsedSeconds >= int.MaxValue / 1000L)
            {
                return 0;
            }

            long remainder = elapsedTicks % Stopwatch.Frequency;
            long elapsedMilliseconds = elapsedSeconds * 1000L
                + remainder * 1000L / Stopwatch.Frequency;
            if (elapsedMilliseconds >= timeoutMs)
            {
                return 0;
            }

            return timeoutMs - (int)elapsedMilliseconds;
        }

        private DateTime GetTimestampSafely()
        {
            if (Volatile.Read(ref _timestampProviderFailed) != 0)
            {
                return DateTime.UtcNow;
            }

            try
            {
                return _instanceTimestampProvider();
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
                if (Interlocked.CompareExchange(ref _timestampProviderFailed, 1, 0) == 0)
                {
                    Interlocked.Increment(ref _timestampProviderFailureCount);
                    EmergencyLogger.TryWrite("The logger timestamp provider failed and was quarantined; UTC system time will be used.", exception);
                }

                return DateTime.UtcNow;
            }
        }

        private int EstimateRetainedCharacters(int messageCharacters, string category, string filePath, string memberName)
        {
            long total = Math.Min(Math.Max(messageCharacters, 0), _processingOptions.MaxMessageCharacters);
            total += Math.Min(category?.Length ?? 0, _processingOptions.MaxCategoryCharacters);
            total += Math.Min(filePath?.Length ?? 0, _processingOptions.MaxSourcePathCharacters);
            total += Math.Min(memberName?.Length ?? 0, _processingOptions.MaxMemberNameCharacters);
            return total >= int.MaxValue ? int.MaxValue : (int)total;
        }

        private void DetachOversizedBuilder(ref StringBuilder builder, out string boundedMessage, out bool truncated)
        {
            boundedMessage = null;
            truncated = false;
            if (builder == null || builder.Capacity <= _processingOptions.MaxMessageCharacters)
            {
                return;
            }

            int length = Math.Min(builder.Length, _processingOptions.MaxMessageCharacters);
            truncated = builder.Length > length;
            boundedMessage = builder.ToString(0, length);
            StringBuilderPool.Return(builder);
            builder = null;
        }
    }
}
