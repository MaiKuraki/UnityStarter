using System;
using System.Runtime.CompilerServices;
using System.Threading;
using CycloneGames.Cheat.Core;
using CycloneGames.Logging;
using Cysharp.Threading.Tasks;
using VitalRouter;

#if ENABLE_CHEAT
using System.Collections.Generic;
#endif

namespace CycloneGames.Cheat.Runtime
{
#if ENABLE_CHEAT
    public sealed class CheatCommandRuntime : ICheatCommandRuntime, ICheatCommandAdmissionPublisher, ICheatLogWriterConfigurable
    {
        private readonly struct CommandStateKey : IEquatable<CommandStateKey>
        {
            public readonly string CommandId;
            public readonly Router Router;
            public readonly RuntimeTypeHandle CommandTypeHandle;
            public readonly long Sequence;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public CommandStateKey(string commandId, Router router, RuntimeTypeHandle commandTypeHandle, long sequence)
            {
                CommandId = commandId;
                Router = router;
                CommandTypeHandle = commandTypeHandle;
                Sequence = sequence;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool Equals(CommandStateKey other)
            {
                return Sequence == other.Sequence
                    && string.Equals(CommandId, other.CommandId, StringComparison.Ordinal)
                    && ReferenceEquals(Router, other.Router)
                    && CommandTypeHandle.Equals(other.CommandTypeHandle);
            }

            public override bool Equals(object obj)
            {
                return obj is CommandStateKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(CommandId);
                    hash = (hash * 31) + (Router != null ? RuntimeHelpers.GetHashCode(Router) : 0);
                    hash = (hash * 31) + CommandTypeHandle.GetHashCode();
                    hash = (hash * 31) + Sequence.GetHashCode();
                    return hash;
                }
            }
        }

        private sealed class CommandExecutionState : IDisposable
        {
            private readonly CancellationTokenSource _cancellationTokenSource;
            private readonly object _lifecycleLock = new object();
            private bool _cancellationStarted;
            private bool _cancellationCompleted;
            private bool _disposeRequested;
            private bool _disposed;

            public CommandExecutionState()
            {
                _cancellationTokenSource = new CancellationTokenSource();
            }

            public CancellationToken Token => _cancellationTokenSource.Token;

            public bool TryBeginCancellation()
            {
                lock (_lifecycleLock)
                {
                    if (_disposed || _cancellationStarted)
                    {
                        return false;
                    }

                    _cancellationStarted = true;
                    return true;
                }
            }

            public bool ExecuteCancellation(LogChannel log)
            {
                bool cancelled = false;
                try
                {
                    cancelled = true;
                    _cancellationTokenSource.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    cancelled = false;
                }
                catch (Exception exception)
                {
                    try
                    {
                        log.Error(exception, "A cheat command cancellation callback failed.");
                    }
                    catch
                    {
                        // Cancellation delivery must continue for the remaining bounded snapshot.
                    }
                }
                finally
                {
                    CompleteCancellation();
                }

                return cancelled;
            }

            public void Dispose()
            {
                bool disposeSource;
                lock (_lifecycleLock)
                {
                    if (_disposed)
                    {
                        return;
                    }

                    if (_cancellationStarted && !_cancellationCompleted)
                    {
                        _disposeRequested = true;
                        return;
                    }

                    _disposed = true;
                    disposeSource = true;
                }

                if (disposeSource)
                {
                    _cancellationTokenSource.Dispose();
                }
            }

            private void CompleteCancellation()
            {
                bool disposeSource = false;
                lock (_lifecycleLock)
                {
                    _cancellationCompleted = true;
                    if (_disposeRequested && !_disposed)
                    {
                        _disposed = true;
                        disposeSource = true;
                    }
                }

                if (disposeSource)
                {
                    _cancellationTokenSource.Dispose();
                }
            }
        }

        private const string ErrCommandIdNullOrEmpty = "[CheatCommandRuntime] CommandId cannot be null or empty.";
        private const string ErrClassArgPrefix = "[CheatCommandRuntime] Argument for command '";
        private const string ErrClassArgSuffix = "' cannot be null.";

        public const int DefaultMaximumConcurrentCommandCount = 256;
        public const int AbsoluteMaximumConcurrentCommandCount = 4096;

        private readonly Dictionary<CommandStateKey, CommandExecutionState> _commandStates =
            new Dictionary<CommandStateKey, CommandExecutionState>();
        private readonly object _admissionLock = new object();

        private ILogWriter _logWriter;
        private long _publishedCommandCount;
        private long _completedCommandCount;
        private long _droppedDuplicateCount;
        private long _cancelRequestedCount;
        private long _faultedCommandCount;
        private long _capacityRejectedCommandCount;
        private long _parallelSequence;
        private int _reservedCommandCount;
        private int _disposed;
        private readonly int _maximumConcurrentCommandCount;

        public CheatCommandRuntime()
            : this(DefaultMaximumConcurrentCommandCount, null)
        {
        }

        public CheatCommandRuntime(int maximumConcurrentCommandCount)
            : this(maximumConcurrentCommandCount, null)
        {
        }

        public CheatCommandRuntime(
            int maximumConcurrentCommandCount,
            ILogWriter logWriter)
        {
            if (maximumConcurrentCommandCount <= 0 ||
                maximumConcurrentCommandCount > AbsoluteMaximumConcurrentCommandCount)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumConcurrentCommandCount));
            }

            _logWriter = logWriter;
            _maximumConcurrentCommandCount = maximumConcurrentCommandCount;
        }

        public bool IsEnabled => Volatile.Read(ref _disposed) == 0;

        public ILogWriter LogWriter
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Volatile.Read(ref _logWriter) ?? LogRuntime.Writer;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => Volatile.Write(ref _logWriter, value);
        }

        public int RunningCommandCount
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Volatile.Read(ref _reservedCommandCount);
        }

        public int MaximumConcurrentCommandCount => _maximumConcurrentCommandCount;

        public CheatRuntimeMetrics Metrics
        {
            get
            {
                return new CheatRuntimeMetrics(
                    RunningCommandCount,
                    Interlocked.Read(ref _publishedCommandCount),
                    Interlocked.Read(ref _completedCommandCount),
                    Interlocked.Read(ref _droppedDuplicateCount),
                    Interlocked.Read(ref _cancelRequestedCount),
                    Interlocked.Read(ref _faultedCommandCount),
                    Interlocked.Read(ref _capacityRejectedCommandCount),
                    _maximumConcurrentCommandCount);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public UniTask PublishAsync(string commandId, Router router = null)
        {
            if (!ValidateCommandId(commandId))
            {
                return UniTask.CompletedTask;
            }

            return PublishAsync(new CheatCommand(commandId), new CheatCommandExecutionOptions(router));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public UniTask PublishAsync<T>(string commandId, T arg, Router router = null) where T : struct
        {
            if (!ValidateCommandId(commandId))
            {
                return UniTask.CompletedTask;
            }

            return PublishAsync(new CheatCommand<T>(commandId, arg), new CheatCommandExecutionOptions(router));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public UniTask PublishAsync<T1, T2>(string commandId, T1 arg1, T2 arg2, Router router = null)
            where T1 : struct
            where T2 : struct
        {
            if (!ValidateCommandId(commandId))
            {
                return UniTask.CompletedTask;
            }

            return PublishAsync(new CheatCommand<T1, T2>(commandId, arg1, arg2), new CheatCommandExecutionOptions(router));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public UniTask PublishAsync<T1, T2, T3>(string commandId, T1 arg1, T2 arg2, T3 arg3, Router router = null)
            where T1 : struct
            where T2 : struct
            where T3 : struct
        {
            if (!ValidateCommandId(commandId))
            {
                return UniTask.CompletedTask;
            }

            return PublishAsync(new CheatCommand<T1, T2, T3>(commandId, arg1, arg2, arg3), new CheatCommandExecutionOptions(router));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public UniTask PublishClassAsync<T>(string commandId, T arg, Router router = null) where T : class
        {
            if (!ValidateCommandId(commandId))
            {
                return UniTask.CompletedTask;
            }

            if (arg == null)
            {
                CreateLogChannel().Error(string.Concat(ErrClassArgPrefix, commandId, ErrClassArgSuffix));
                return UniTask.CompletedTask;
            }

            return PublishAsync(new CheatCommandClass<T>(commandId, arg), new CheatCommandExecutionOptions(router));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public UniTask PublishAsync<TCommand>(TCommand command, CheatCommandExecutionOptions options = default)
            where TCommand : ICheatCommand
        {
            return TryPublishAsync(command, options).AsUniTask();
        }

        public async UniTask<CheatCommandPublishResult> TryPublishAsync<TCommand>(
            TCommand command,
            CheatCommandExecutionOptions options = default)
            where TCommand : ICheatCommand
        {
            ThrowIfDisposed();

            if (!ValidateCommandId(command.CommandId))
            {
                return CheatCommandPublishResult.InvalidCommand;
            }

            Router targetRouter = options.Router ?? Router.Default;
            long sequence = options.DuplicatePolicy == CheatDuplicatePolicy.AllowParallel
                ? Interlocked.Increment(ref _parallelSequence)
                : 0;
            var key = new CommandStateKey(command.CommandId, targetRouter, typeof(TCommand).TypeHandle, sequence);
            CheatCommandPublishResult admissionResult = TryAdmitCommand(key, out CommandExecutionState state);
            if (admissionResult != CheatCommandPublishResult.Published)
            {
                return admissionResult;
            }

            try
            {
                try
                {
                    await targetRouter.PublishAsync(command, state.Token);
                    Interlocked.Increment(ref _completedCommandCount);
                }
                catch (OperationCanceledException)
                {
                    Interlocked.Increment(ref _completedCommandCount);
                }
                catch (Exception exception)
                {
                    Interlocked.Increment(ref _faultedCommandCount);
                    CreateLogChannel().Error(exception, "A cheat command execution failed.");
                }
            }
            finally
            {
                ReleaseCommandState(key, state);
                state.Dispose();
            }

            return CheatCommandPublishResult.Published;
        }

        public bool IsCommandRunning(string commandId)
        {
            if (string.IsNullOrEmpty(commandId))
            {
                return false;
            }

            lock (_admissionLock)
            {
                foreach (var pair in _commandStates)
                {
                    if (string.Equals(pair.Key.CommandId, commandId, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CancelCommand(string commandId)
        {
            CancelCommand(commandId, null);
        }

        public void CancelCommand(string commandId, Router router)
        {
            if (string.IsNullOrEmpty(commandId))
            {
                return;
            }

            var cancellationRequests = new List<CommandExecutionState>();
            lock (_admissionLock)
            {
                foreach (var pair in _commandStates)
                {
                    if (ShouldCancel(pair.Key, commandId, router)
                        && pair.Value.TryBeginCancellation())
                    {
                        cancellationRequests.Add(pair.Value);
                    }
                }
            }

            ExecuteCancellationRequests(cancellationRequests, countRequests: true);
        }

        public void ClearAll()
        {
            var cancellationRequests = new List<CommandExecutionState>();
            lock (_admissionLock)
            {
                foreach (var pair in _commandStates)
                {
                    if (pair.Value.TryBeginCancellation())
                    {
                        cancellationRequests.Add(pair.Value);
                    }
                }
            }

            ExecuteCancellationRequests(cancellationRequests, countRequests: false);
        }

        public void Dispose()
        {
            var cancellationRequests = new List<CommandExecutionState>();
            lock (_admissionLock)
            {
                if (_disposed != 0)
                {
                    return;
                }

                Volatile.Write(ref _disposed, 1);
                foreach (var pair in _commandStates)
                {
                    if (pair.Value.TryBeginCancellation())
                    {
                        cancellationRequests.Add(pair.Value);
                    }
                }

                _commandStates.Clear();
                _reservedCommandCount = 0;
            }

            ExecuteCancellationRequests(cancellationRequests, countRequests: false);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool ShouldCancel(CommandStateKey key, string commandId, Router router)
        {
            return string.Equals(key.CommandId, commandId, StringComparison.Ordinal)
                && (router == null || ReferenceEquals(key.Router, router));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ValidateCommandId(string commandId)
        {
            if (!string.IsNullOrEmpty(commandId))
            {
                return true;
            }

            CreateLogChannel().Error(ErrCommandIdNullOrEmpty);
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(CheatCommandRuntime));
            }
        }

        private CheatCommandPublishResult TryAdmitCommand(
            CommandStateKey key,
            out CommandExecutionState state)
        {
            state = null;
            try
            {
                lock (_admissionLock)
                {
                    ThrowIfDisposed();

                    if (ContainsState(key))
                    {
                        Interlocked.Increment(ref _droppedDuplicateCount);
                        return CheatCommandPublishResult.DuplicateRejected;
                    }

                    if (_reservedCommandCount >= _maximumConcurrentCommandCount)
                    {
                        Interlocked.Increment(ref _capacityRejectedCommandCount);
                        return CheatCommandPublishResult.CapacityRejected;
                    }

                    state = new CommandExecutionState();
                    RegisterState(key, state);
                    _reservedCommandCount++;
                    Interlocked.Increment(ref _publishedCommandCount);
                    return CheatCommandPublishResult.Published;
                }
            }
            catch
            {
                state?.Dispose();
                state = null;
                throw;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ContainsState(CommandStateKey key)
        {
            return _commandStates.ContainsKey(key);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RegisterState(CommandStateKey key, CommandExecutionState state)
        {
            _commandStates.Add(key, state);
        }

        private void ReleaseCommandState(CommandStateKey key, CommandExecutionState state)
        {
            lock (_admissionLock)
            {
                if (TryRemoveState(key, state))
                {
                    _reservedCommandCount--;
                }
            }
        }

        private bool TryRemoveState(CommandStateKey key, CommandExecutionState state)
        {
            if (_commandStates.TryGetValue(key, out var current) && ReferenceEquals(current, state))
            {
                _commandStates.Remove(key);
                return true;
            }

            return false;
        }

        private void ExecuteCancellationRequests(
            List<CommandExecutionState> cancellationRequests,
            bool countRequests)
        {
            LogChannel log = CreateLogChannel();
            for (int i = 0; i < cancellationRequests.Count; i++)
            {
                if (cancellationRequests[i].ExecuteCancellation(log) && countRequests)
                {
                    Interlocked.Increment(ref _cancelRequestedCount);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private LogChannel CreateLogChannel()
        {
            return CheatRuntimeLog.CreateOptional(Volatile.Read(ref _logWriter));
        }
    }
#else
    public sealed class CheatCommandRuntime : ICheatCommandRuntime, ICheatCommandAdmissionPublisher, ICheatLogWriterConfigurable
    {
        private ILogWriter _logWriter;
        private readonly int _maximumConcurrentCommandCount;

        public const int DefaultMaximumConcurrentCommandCount = 256;
        public const int AbsoluteMaximumConcurrentCommandCount = 4096;

        public CheatCommandRuntime()
            : this(DefaultMaximumConcurrentCommandCount, null)
        {
        }

        public CheatCommandRuntime(int maximumConcurrentCommandCount)
            : this(maximumConcurrentCommandCount, null)
        {
        }

        public CheatCommandRuntime(
            int maximumConcurrentCommandCount,
            ILogWriter logWriter)
        {
            if (maximumConcurrentCommandCount <= 0 ||
                maximumConcurrentCommandCount > AbsoluteMaximumConcurrentCommandCount)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumConcurrentCommandCount));
            }

            _logWriter = logWriter;
            _maximumConcurrentCommandCount = maximumConcurrentCommandCount;
        }

        public bool IsEnabled => false;

        public ILogWriter LogWriter
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _logWriter ?? LogRuntime.Writer;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _logWriter = value;
        }

        public int RunningCommandCount => 0;

        public int MaximumConcurrentCommandCount => _maximumConcurrentCommandCount;

        public CheatRuntimeMetrics Metrics => new CheatRuntimeMetrics(
            0,
            0L,
            0L,
            0L,
            0L,
            0L,
            0L,
            _maximumConcurrentCommandCount);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public UniTask PublishAsync(string commandId, Router router = null)
        {
            return UniTask.CompletedTask;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public UniTask PublishAsync<T>(string commandId, T arg, Router router = null) where T : struct
        {
            return UniTask.CompletedTask;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public UniTask PublishAsync<T1, T2>(string commandId, T1 arg1, T2 arg2, Router router = null)
            where T1 : struct
            where T2 : struct
        {
            return UniTask.CompletedTask;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public UniTask PublishAsync<T1, T2, T3>(string commandId, T1 arg1, T2 arg2, T3 arg3, Router router = null)
            where T1 : struct
            where T2 : struct
            where T3 : struct
        {
            return UniTask.CompletedTask;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public UniTask PublishClassAsync<T>(string commandId, T arg, Router router = null) where T : class
        {
            return UniTask.CompletedTask;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public UniTask PublishAsync<TCommand>(TCommand command, CheatCommandExecutionOptions options = default)
            where TCommand : ICheatCommand
        {
            return UniTask.CompletedTask;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public UniTask<CheatCommandPublishResult> TryPublishAsync<TCommand>(
            TCommand command,
            CheatCommandExecutionOptions options = default)
            where TCommand : ICheatCommand
        {
            return UniTask.FromResult(CheatCommandPublishResult.Disabled);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsCommandRunning(string commandId)
        {
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CancelCommand(string commandId)
        {
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CancelCommand(string commandId, Router router)
        {
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ClearAll()
        {
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
        }
    }
#endif
}
