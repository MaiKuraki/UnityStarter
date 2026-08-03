using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using CycloneGames.Logging;
using CycloneGames.Logging.Pipeline.Internal;

namespace CycloneGames.Logging.Pipeline
{
    /// <summary>
    /// Bounded logging backend and sink owner. Producers write through <see cref="ILogWriter"/>
    /// or a bound <see cref="LogChannel"/>; composition roots own this concrete type.
    /// </summary>
    public sealed class LogPipeline : ILogWriter, ILogPipelineMonitor, IDisposable
    {
        private sealed class SinkRegistration
        {
            private const int RetiredMask = 1;
            private const int UsageIncrement = 2;

            internal readonly ILogSink Sink;
            internal int ActiveCountReleased;
            internal int ConsecutiveFailures;
            internal int QuarantinedByFailure;

            private readonly object _quiescenceLock = new object();
            private int _usageState;

            internal SinkRegistration(ILogSink sink)
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
                        int remaining = timeoutMs - unchecked(Environment.TickCount - startTick);
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

        private sealed class SinkReferenceComparer : IEqualityComparer<ILogSink>
        {
            internal static readonly SinkReferenceComparer Instance = new SinkReferenceComparer();

            public bool Equals(ILogSink left, ILogSink right)
            {
                return ReferenceEquals(left, right);
            }

            public int GetHashCode(ILogSink value)
            {
                return RuntimeHelpers.GetHashCode(value);
            }
        }

        private readonly struct ExceptionWriteState
        {
            internal readonly string Message;
            internal readonly Exception Exception;

            internal ExceptionWriteState(string message, Exception exception)
            {
                Message = message;
                Exception = exception;
            }
        }

        private const int DefaultSinkQuiescenceTimeoutMs = 1000;
        private const int MaxOwnedSinks = 256;
        private const int SinkDisposeAttemptCount = 3;
        private static readonly Action<ExceptionWriteState, StringBuilder> ExceptionMessageBuilder = AppendExceptionMessage;

        [ThreadStatic]
        private static LogPipeline _sinkDisposalOwner;

        [ThreadStatic]
        private static int _sinkCallbackDepth;

        private readonly ReaderWriterLockSlim _sinksLock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
        private readonly List<SinkRegistration> _sinks = new List<SinkRegistration>();
        private readonly Dictionary<ILogSink, SinkRegistration> _retiredRegistrations =
            new Dictionary<ILogSink, SinkRegistration>(SinkReferenceComparer.Instance);
        private readonly HashSet<ILogSink> _disposingSinks = new HashSet<ILogSink>(SinkReferenceComparer.Instance);
        private readonly object _dispatchStateLock = new object();
        private readonly object _filterMutationLock = new object();
        private readonly object _shutdownLock = new object();
        private readonly object _sinkDisposalQueueLock = new object();
        private readonly ILogSink[] _sinkDisposalQueue = new ILogSink[MaxOwnedSinks];
        private readonly ILogSink[] _pendingSinkDisposals = new ILogSink[MaxOwnedSinks];
        private readonly ILogProcessor _processor;
        private readonly Func<DateTime> _instanceTimestampProvider;
        private readonly LogPipelineOptions _processingOptions;

        private volatile SinkRegistration[] _sinkSnapshot = Array.Empty<SinkRegistration>();
        private volatile HashSet<string> _allowListSnapshot = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private volatile HashSet<string> _denyListSnapshot = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private volatile LogSeverity _minimumSeverity = LogSeverity.Info;
        private volatile LogCategoryFilterMode _categoryFilter = LogCategoryFilterMode.All;
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
        private int _shutdownCallActive;
        private int _shutdownOwnerThreadId;
        private LogPipelineShutdownResult _lastShutdownResult;
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
        private Exception _fatalFailure;
        private int _quarantinedSinkCount;
        private int _filterCategoryCount;
        private int _filterCharacters;
        private int _timestampProviderFailed;
        private int _messageBuilderFailureEmergencyReported;

        internal LogPipeline(
            Func<LogPipeline, LogPipelineOptions, ILogProcessor> processorFactory,
            LogPipelineOptions processingOptions,
            Func<DateTime> timestampProvider)
        {
            _processingOptions = LogPipelineOptions.CreateValidated(processingOptions);
            _instanceTimestampProvider = timestampProvider ?? (() => DateTime.UtcNow);
            LogEventPool.Prewarm();
            StringBuilderPool.Prewarm();
            _processor = (processorFactory ?? throw new ArgumentNullException(nameof(processorFactory)))(this, _processingOptions);
        }

        public LogSeverity MinimumSeverity
        {
            get => _minimumSeverity;
            set
            {
                if (value < LogSeverity.Trace || value > LogSeverity.None)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                _minimumSeverity = value;
            }
        }

        bool ILogWriter.IsEnabled(LogSeverity severity, string category)
        {
            return CanAccept(severity, category);
        }

        void ILogWriter.Write(
            LogSeverity severity,
            string category,
            string message,
            string filePath,
            int lineNumber,
            string memberName)
        {
            EnqueueMessage(severity, message, category, filePath, lineNumber, memberName);
        }

        void ILogWriter.Write(
            LogSeverity severity,
            string category,
            Action<StringBuilder> messageBuilder,
            string filePath,
            int lineNumber,
            string memberName)
        {
            EnqueueMessage(severity, messageBuilder, category, filePath, lineNumber, memberName);
        }

        void ILogWriter.Write<TState>(
            LogSeverity severity,
            string category,
            TState state,
            Action<TState, StringBuilder> messageBuilder,
            string filePath,
            int lineNumber,
            string memberName)
        {
            EnqueueMessage(severity, state, messageBuilder, category, filePath, lineNumber, memberName);
        }

        void ILogWriter.WriteException(
            LogSeverity severity,
            string category,
            Exception exception,
            string message,
            string filePath,
            int lineNumber,
            string memberName)
        {
            if (exception == null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            EnqueueMessage(
                severity,
                new ExceptionWriteState(message, exception),
                ExceptionMessageBuilder,
                category,
                filePath,
                lineNumber,
                memberName);
        }

        public LogCategoryFilterMode CategoryFilter
        {
            get => _categoryFilter;
            set
            {
                if (value < LogCategoryFilterMode.All || value > LogCategoryFilterMode.DenyList)
                {
                    throw new ArgumentOutOfRangeException(nameof(value));
                }

                _categoryFilter = value;
            }
        }

        /// <summary>
        /// Registers a sink and reports the resulting ownership explicitly. Ownership transfers
        /// to the pipeline only when <see cref="LogSinkRegistrationResult.PipelineOwnsSink"/> is
        /// true. Rejected sinks are never disposed by this method.
        /// </summary>
        public LogSinkRegistrationResult RegisterSink(
            ILogSink sink,
            LogSinkRegistrationMode mode = LogSinkRegistrationMode.AllowMultiple)
        {
            if (sink == null)
            {
                throw new ArgumentNullException(nameof(sink));
            }

            if (mode < LogSinkRegistrationMode.AllowMultiple
                || mode > LogSinkRegistrationMode.UniqueExactType)
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
            }

            _sinksLock.EnterWriteLock();
            try
            {
                bool isStopping = Volatile.Read(ref _lifecycleState) != 0;

                for (int i = 0; i < _sinks.Count; i++)
                {
                    if (ReferenceEquals(_sinks[i].Sink, sink))
                    {
                        return new LogSinkRegistrationResult(
                            isStopping || _sinks[i].IsRetired
                                ? LogSinkRegistrationStatus.AlreadyOwnedByPipeline
                                : LogSinkRegistrationStatus.AlreadyRegistered);
                    }
                }

                if (_retiredRegistrations.ContainsKey(sink) || _disposingSinks.Contains(sink))
                {
                    return new LogSinkRegistrationResult(
                        LogSinkRegistrationStatus.AlreadyOwnedByPipeline);
                }

                if (isStopping)
                {
                    return new LogSinkRegistrationResult(
                        LogSinkRegistrationStatus.RejectedPipelineStopping);
                }

                if (mode == LogSinkRegistrationMode.UniqueExactType)
                {
                    Type type = sink.GetType();
                    for (int i = 0; i < _sinks.Count; i++)
                    {
                        if (!_sinks[i].IsRetired && _sinks[i].Sink.GetType() == type)
                        {
                            return new LogSinkRegistrationResult(
                                LogSinkRegistrationStatus.RejectedDuplicateType);
                        }
                    }
                }

                if (!HasSinkOwnershipCapacityNoLock())
                {
                    return new LogSinkRegistrationResult(
                        LogSinkRegistrationStatus.RejectedCapacity);
                }

                var registration = new SinkRegistration(sink);
                SinkRegistration[] snapshot = CreateSnapshotWithAddedSinkNoLock(registration);
                _sinks.Add(registration);
                _sinkSnapshot = snapshot;
                Interlocked.Increment(ref _activeSinkCount);
                Interlocked.Increment(ref _ownedSinkCount);
                return new LogSinkRegistrationResult(LogSinkRegistrationStatus.Registered);
            }
            finally
            {
                _sinksLock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Removes a sink from future dispatch. A true result means all earlier dispatches
        /// have quiesced and sink ownership has transferred back to the caller. A false
        /// result means the caller must not dispose the sink because it was not registered,
        /// another owner already claimed it, or dispatches have not quiesced yet.
        /// </summary>
        public bool RemoveSink(ILogSink sink, int quiescenceTimeoutMs = DefaultSinkQuiescenceTimeoutMs)
        {
            if (quiescenceTimeoutMs < 0
                || quiescenceTimeoutMs > LogPipelineOptions.MaxSupportedShutdownDrainTimeoutMs)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(quiescenceTimeoutMs),
                    $"quiescenceTimeoutMs must be between 0 and {LogPipelineOptions.MaxSupportedShutdownDrainTimeoutMs}.");
            }

            if (sink == null)
            {
                return true;
            }

            if (IsInsideOwnedSinkCallback)
            {
                return false;
            }

            SinkRegistration removed = null;
            _sinksLock.EnterWriteLock();
            try
            {
                for (int i = 0; i < _sinks.Count; i++)
                {
                    if (!ReferenceEquals(_sinks[i].Sink, sink))
                    {
                        continue;
                    }

                    removed = _sinks[i];
                    SinkRegistration[] snapshot = CreateSnapshotWithoutSinkNoLock(i);
                    _retiredRegistrations.Add(sink, removed);
                    if (!removed.Retire())
                    {
                        _retiredRegistrations.Remove(sink);
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
                    _retiredRegistrations.TryGetValue(sink, out removed);
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
                if (_retiredRegistrations.TryGetValue(sink, out SinkRegistration tracked)
                    && ReferenceEquals(tracked, removed))
                {
                    _retiredRegistrations.Remove(sink);
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

        public void ClearSinks()
        {
            if (IsInsideOwnedSinkCallback)
            {
                throw new InvalidOperationException("ClearSinks cannot run from an ILogSink callback.");
            }

            lock (_shutdownLock)
            {
                ThrowIfStopping();
                DetachAllSinks();
                WaitForActiveDispatches(DefaultSinkQuiescenceTimeoutMs);
            }
        }

        private void DetachAllSinks()
        {
            List<ILogSink> toDispose;
            _sinksLock.EnterWriteLock();
            try
            {
                toDispose = new List<ILogSink>(_sinks.Count + _retiredRegistrations.Count);
                for (int i = 0; i < _sinks.Count; i++)
                {
                    toDispose.Add(_sinks[i].Sink);
                }

                foreach (KeyValuePair<ILogSink, SinkRegistration> pair in _retiredRegistrations)
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

                foreach (KeyValuePair<ILogSink, SinkRegistration> pair in _retiredRegistrations)
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

        public void AddAllowedCategory(string category)
        {
            MutateCategorySet(category, true, true);
        }

        public void RemoveAllowedCategory(string category)
        {
            MutateCategorySet(category, true, false);
        }

        public void AddDeniedCategory(string category)
        {
            MutateCategorySet(category, false, true);
        }

        public void RemoveDeniedCategory(string category)
        {
            MutateCategorySet(category, false, false);
        }

        internal void EnqueueMessage(LogSeverity level, string message, string category, string filePath, int lineNumber, string memberName)
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

            LogEvent entry = null;
            bool reservationOwned = true;
            try
            {
                entry = LogEventPool.Get();
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
                    LogEventPool.Return(entry);
                }
            }
        }

        internal void EnqueueMessage(LogSeverity level, Action<StringBuilder> messageBuilder, string category, string filePath, int lineNumber, string memberName)
        {
            EnqueueMessage(level, messageBuilder, InvokeMessageBuilder, category, filePath, lineNumber, memberName);
        }

        internal void EnqueueMessage<T>(LogSeverity level, T state, Action<T, StringBuilder> messageBuilder, string category, string filePath, int lineNumber, string memberName)
        {
            if (!CanAccept(level, category))
            {
                return;
            }

            // Reserve the largest queue-owned payload before invoking user code. The callback
            // itself remains caller-controlled, but concurrent pipeline-owned builders cannot
            // oversubscribe the configured retained queue budget.
            int estimate = EstimateRetainedCharacters(_processingOptions.MaxMessageCharacters, category, filePath, memberName);
            if (!_processor.TryReserve(level, estimate, false, out int reservedCharacters))
            {
                return;
            }

            StringBuilder builder = null;
            string boundedMessage = null;
            bool builderTruncated = false;
            LogEvent entry = null;
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
                        EmergencyLogWriter.TryWrite(
                            "A log message builder callback failed; bounded diagnostic entries will be emitted and further emergency reports are suppressed.",
                            exception);
                    }
                }

                DetachOversizedBuilder(ref builder, out boundedMessage, out builderTruncated);
                entry = LogEventPool.Get();
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
                    LogEventPool.Return(entry);
                }
                else if (builder != null)
                {
                    StringBuilderPool.Return(builder);
                }
            }
        }

        internal void DispatchToSinks(LogEvent message)
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
                            registration.Sink.Emit(message);
                            Volatile.Write(ref registration.ConsecutiveFailures, 0);
                        }
                        catch (OutOfMemoryException exception)
                        {
                            RecordFatalFailure(exception);
                            throw;
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
                    if (!(registration.Sink is IMaintainableLogSink maintainable)
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
                        catch (OutOfMemoryException exception)
                        {
                            RecordFatalFailure(exception);
                            throw;
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
            ThrowIfFaulted();
            _processor.Pump(maxItems, -1);
        }

        internal void PumpWithinBudget(int maxItems, int budgetMilliseconds)
        {
            ThrowIfFaulted();
            _processor.Pump(maxItems, Math.Max(budgetMilliseconds, 0));
        }

        public bool TryFlush(LogFlushMode mode = LogFlushMode.Buffered, int timeoutMs = -1)
        {
            ValidateFlushMode(mode, nameof(mode));
            ValidateLifecycleTimeout(timeoutMs, nameof(timeoutMs));

            if (IsInsideOwnedSinkCallback)
            {
                return false;
            }

            ThrowIfFaulted();

            if (timeoutMs == -1)
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

        public LogPipelineStatistics GetStatistics()
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

        public LogPipelineShutdownResult Shutdown(LogFlushMode flushMode = LogFlushMode.Buffered, int timeoutMs = -1)
        {
            ValidateFlushMode(flushMode, nameof(flushMode));
            ValidateLifecycleTimeout(timeoutMs, nameof(timeoutMs));

            int currentThreadId = Environment.CurrentManagedThreadId;
            if (IsInsideOwnedSinkCallback
                || (Volatile.Read(ref _shutdownCallActive) != 0
                    && Volatile.Read(ref _shutdownOwnerThreadId) == currentThreadId))
            {
                return new LogPipelineShutdownResult(
                    LogPipelineShutdownStatus.InProgress,
                    Interlocked.Read(ref _shutdownDroppedMessageCount),
                    _shutdownFlushAttempted && _shutdownSinksFlushed);
            }

            lock (_shutdownLock)
            {
                Volatile.Write(ref _shutdownCallActive, 1);
                Volatile.Write(ref _shutdownOwnerThreadId, currentThreadId);
                try
                {
                    if (timeoutMs == -1)
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
                        LogPipelineShutdownResult processorResult = _processor.Shutdown(
                            GetRemainingTimeout(startTimestamp, timeoutMs));
                        _shutdownDroppedMessageCount = Math.Max(
                            _shutdownDroppedMessageCount,
                            processorResult.DroppedMessageCount);
                        if (!processorResult.IsComplete || !_processor.IsStopped)
                        {
                            return new LogPipelineShutdownResult(
                                LogPipelineShutdownStatus.TimedOut,
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
                        DetachAllSinks();
                        _shutdownSinksDetached = true;
                    }

                    bool dispatchesCompleted = WaitForActiveDispatches(
                        GetRemainingTimeout(startTimestamp, timeoutMs));
                    bool disposalExecutorStopped = StopSinkDisposalExecutor(
                        dispatchesCompleted ? GetRemainingTimeout(startTimestamp, timeoutMs) : 0);
                    if (!dispatchesCompleted || !disposalExecutorStopped)
                    {
                        return new LogPipelineShutdownResult(
                            LogPipelineShutdownStatus.TimedOut,
                            _shutdownDroppedMessageCount,
                            _shutdownSinksFlushed);
                    }

                    _processor.Dispose();
                    bool hasFailures = IsFaulted
                        || !_shutdownSinksFlushed
                        || Interlocked.Read(ref _sinkDisposalFailureCount) != 0;
                    LogPipelineShutdownStatus status = hasFailures
                        ? LogPipelineShutdownStatus.CompletedWithFailures
                        : _shutdownDroppedMessageCount > 0
                            ? LogPipelineShutdownStatus.CompletedWithDrops
                            : LogPipelineShutdownStatus.Completed;
                    _lastShutdownResult = new LogPipelineShutdownResult(
                        status,
                        _shutdownDroppedMessageCount,
                        _shutdownSinksFlushed);
                    Volatile.Write(ref _lifecycleState, 2);
                    return _lastShutdownResult;
                }
                finally
                {
                    Volatile.Write(ref _shutdownOwnerThreadId, 0);
                    Volatile.Write(ref _shutdownCallActive, 0);
                }
            }
        }

        public void Dispose()
        {
            LogPipelineShutdownResult result = Shutdown();
            if (!result.IsComplete)
            {
                EmergencyLogWriter.TryWrite(
                    "LogPipeline.Dispose did not complete. Keep the instance and retry Shutdown after releasing blocked sinks.");
            }
        }

        public bool IsFaulted => Volatile.Read(ref _fatalFailure) != null;

        private static void InvokeMessageBuilder(Action<StringBuilder> append, StringBuilder builder)
        {
            append?.Invoke(builder);
        }

        private static void AppendExceptionMessage(ExceptionWriteState state, StringBuilder builder)
        {
            if (!string.IsNullOrEmpty(state.Message))
            {
                builder.Append(state.Message);
                builder.AppendLine();
            }

            builder.Append(state.Exception);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool CanAccept(LogSeverity level, string category)
        {
            ThrowIfFaulted();
            return Volatile.Read(ref _lifecycleState) == 0
                && Volatile.Read(ref _activeSinkCount) > 0
                && ShouldLog(level, category);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ShouldLog(LogSeverity level, string category)
        {
            if (level < LogSeverity.Trace || level >= LogSeverity.None || level < _minimumSeverity)
            {
                return false;
            }

            LogCategoryFilterMode filter = _categoryFilter;
            if (filter == LogCategoryFilterMode.All)
            {
                return true;
            }

            if (!string.IsNullOrEmpty(category)
                && category.Length > _processingOptions.MaxCategoryCharacters)
            {
                return false;
            }

            if (filter == LogCategoryFilterMode.AllowList)
            {
                return !string.IsNullOrEmpty(category) && _allowListSnapshot.Contains(category);
            }

            return string.IsNullOrEmpty(category) || !_denyListSnapshot.Contains(category);
        }

        private void MutateCategorySet(string category, bool allowList, bool add)
        {
            if (string.IsNullOrEmpty(category))
            {
                return;
            }

            lock (_filterMutationLock)
            {
                HashSet<string> current = allowList ? _allowListSnapshot : _denyListSnapshot;
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
                    if (allowList)
                    {
                        _allowListSnapshot = reduced;
                    }
                    else
                    {
                        _denyListSnapshot = reduced;
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
                    throw new InvalidOperationException("The configured pipeline category-filter memory budget was exhausted.");
                }

                var updated = new HashSet<string>(current, StringComparer.OrdinalIgnoreCase);
                updated.Add(category);
                _filterCategoryCount++;
                _filterCharacters += category.Length;
                if (allowList)
                {
                    _allowListSnapshot = updated;
                }
                else
                {
                    _denyListSnapshot = updated;
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
            _sinkCallbackDepth++;
        }

        private void EndDispatch()
        {
            _sinkCallbackDepth--;
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
            if (IsInsideOwnedSinkCallback)
            {
                return false;
            }

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

        private static bool IsInsideSinkCallback => _sinkCallbackDepth != 0;

        private bool IsInsideOwnedSinkCallback =>
            IsInsideSinkCallback || ReferenceEquals(_sinkDisposalOwner, this);

        internal void RecordFatalFailure(Exception exception)
        {
            if (exception != null)
            {
                Interlocked.CompareExchange(ref _fatalFailure, exception, null);
            }
        }

        private void ThrowIfFaulted()
        {
            Exception failure = Volatile.Read(ref _fatalFailure);
            if (failure != null)
            {
                throw failure;
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

            var disposalBatch = new List<ILogSink>(1) { registration.Sink };

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

            EmergencyLogWriter.TryWrite(
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
                    if (!(registration.Sink is IFlushableLogSink flushable)
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
                        catch (OutOfMemoryException exception)
                        {
                            RecordFatalFailure(exception);
                            throw;
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

        private void ScheduleSinkDisposals(List<ILogSink> sinks)
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

        private void CompleteSinkDisposal(ILogSink sink)
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

        private void QueueSinkDisposals(List<ILogSink> sinks)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            OutOfMemoryException fatalFailure = null;
            for (int i = 0; i < sinks.Count; i++)
            {
                try
                {
                    TryDisposeSink(sinks[i]);
                }
                catch (OutOfMemoryException exception)
                {
                    if (fatalFailure == null)
                    {
                        fatalFailure = exception;
                    }
                }
                finally
                {
                    CompleteSinkDisposal(sinks[i]);
                }
            }

            if (fatalFailure != null)
            {
                throw fatalFailure;
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
                    ILogSink sink;
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

                    try
                    {
                        TryDisposeSink(sink);
                    }
                    catch (OutOfMemoryException)
                    {
                        // TryDisposeSink records the terminal failure. Continue bounded ownership cleanup.
                    }
                    finally
                    {
                        CompleteSinkDisposal(sink);
                    }
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

        private void EnqueueSinkDisposalNoLock(ILogSink sink)
        {
            if (_sinkDisposalQueueCount >= _sinkDisposalQueue.Length)
            {
                throw new InvalidOperationException("Sink disposal queue capacity was exceeded.");
            }

            int tail = (_sinkDisposalQueueHead + _sinkDisposalQueueCount) % _sinkDisposalQueue.Length;
            _sinkDisposalQueue[tail] = sink;
            _sinkDisposalQueueCount++;
        }

        private ILogSink DequeueSinkDisposalNoLock()
        {
            ILogSink sink = _sinkDisposalQueue[_sinkDisposalQueueHead];
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
                    Name = "LogPipeline.SinkDisposal",
                    IsBackground = true
                };
                _sinkDisposalWorkerOwnsQueue = true;
                _sinkDisposalThread.Start();
                return true;
            }
            catch (OutOfMemoryException exception)
            {
                RecordFatalFailure(exception);
                _sinkDisposalWorkerOwnsQueue = false;
                _sinkDisposalThread = null;
                return false;
            }
            catch (Exception exception)
            {
                _sinkDisposalWorkerOwnsQueue = false;
                _sinkDisposalThread = null;
                EmergencyLogWriter.TryWrite("Sink disposal executor was unavailable; disposal is running synchronously.", exception);
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
            OutOfMemoryException fatalFailure = null;
            while (true)
            {
                ILogSink sink;
                lock (_sinkDisposalQueueLock)
                {
                    if (_sinkDisposalQueueCount == 0)
                    {
                        break;
                    }

                    sink = DequeueSinkDisposalNoLock();
                }

                try
                {
                    TryDisposeSink(sink);
                }
                catch (OutOfMemoryException exception)
                {
                    if (fatalFailure == null)
                    {
                        fatalFailure = exception;
                    }
                }
                finally
                {
                    CompleteSinkDisposal(sink);
                }
            }

            if (fatalFailure != null)
            {
                throw fatalFailure;
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

        private void TryDisposeSink(ILogSink sink)
        {
            LogPipeline previousDisposalOwner = _sinkDisposalOwner;
            _sinkDisposalOwner = this;
            Exception lastException = null;
            int attemptCount = sink is IIdempotentLogSinkDisposal ? SinkDisposeAttemptCount : 1;
            try
            {
                for (int attempt = 0; attempt < attemptCount; attempt++)
                {
                    try
                    {
                        sink.Dispose();
                        return;
                    }
                    catch (OutOfMemoryException exception)
                    {
                        RecordFatalFailure(exception);
                        throw;
                    }
                    catch (Exception exception)
                    {
                        lastException = exception;
                    }
                }

                Interlocked.Increment(ref _sinkDisposalFailureCount);
                EmergencyLogWriter.TryWrite("A log sink failed all bounded disposal attempts.", lastException);
            }
            finally
            {
                _sinkDisposalOwner = previousDisposalOwner;
            }
        }

        private void ThrowIfStopping()
        {
            if (Volatile.Read(ref _lifecycleState) != 0)
            {
                throw new ObjectDisposedException(nameof(LogPipeline));
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

        private static void ValidateFlushMode(LogFlushMode mode, string parameterName)
        {
            if (mode != LogFlushMode.Buffered && mode != LogFlushMode.Durable)
            {
                throw new ArgumentOutOfRangeException(parameterName, "Unknown flush mode.");
            }
        }

        private static void ValidateLifecycleTimeout(int timeoutMs, string parameterName)
        {
            if (timeoutMs < -1 || timeoutMs > LogPipelineOptions.MaxSupportedShutdownDrainTimeoutMs)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    $"Timeout must be -1 or between 0 and {LogPipelineOptions.MaxSupportedShutdownDrainTimeoutMs}.");
            }
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
                    EmergencyLogWriter.TryWrite("The pipeline timestamp provider failed and was quarantined; UTC system time will be used.", exception);
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
