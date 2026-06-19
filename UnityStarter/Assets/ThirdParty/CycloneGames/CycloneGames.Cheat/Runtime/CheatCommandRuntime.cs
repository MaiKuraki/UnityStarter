using System;
using System.Runtime.CompilerServices;
using System.Threading;
using CycloneGames.Cheat.Core;
using Cysharp.Threading.Tasks;
using VitalRouter;

#if ENABLE_CHEAT
using System.Collections.Concurrent;
using System.Collections.Generic;
#endif

namespace CycloneGames.Cheat.Runtime
{
#if ENABLE_CHEAT
    public sealed class CheatCommandRuntime : ICheatCommandRuntime
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
            private int _disposed;

            public CommandExecutionState()
            {
                _cancellationTokenSource = new CancellationTokenSource();
            }

            public CancellationToken Token => _cancellationTokenSource.Token;

            public bool Cancel(ICheatLogger logger)
            {
                if (Volatile.Read(ref _disposed) != 0)
                {
                    return false;
                }

                if (_cancellationTokenSource.IsCancellationRequested)
                {
                    return false;
                }

                try
                {
                    _cancellationTokenSource.Cancel();
                    return true;
                }
                catch (ObjectDisposedException)
                {
                    return false;
                }
                catch (Exception exception)
                {
                    logger?.LogException(exception);
                    return false;
                }
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _disposed, 1) != 0)
                {
                    return;
                }

                _cancellationTokenSource.Dispose();
            }
        }

        private const string ErrCommandIdNullOrEmpty = "[CheatCommandRuntime] CommandId cannot be null or empty.";
        private const string ErrClassArgPrefix = "[CheatCommandRuntime] Argument for command '";
        private const string ErrClassArgSuffix = "' cannot be null.";

#if UNITY_WEBGL
        private readonly Dictionary<CommandStateKey, CommandExecutionState> _commandStates =
            new Dictionary<CommandStateKey, CommandExecutionState>();
        private readonly object _stateLock = new object();
#else
        private readonly ConcurrentDictionary<CommandStateKey, CommandExecutionState> _commandStates =
            new ConcurrentDictionary<CommandStateKey, CommandExecutionState>();
#endif

        private ICheatLogger _logger;
        private long _publishedCommandCount;
        private long _completedCommandCount;
        private long _droppedDuplicateCount;
        private long _cancelRequestedCount;
        private long _faultedCommandCount;
        private long _parallelSequence;
        private int _disposed;

        public CheatCommandRuntime(ICheatLogger logger = null)
        {
            _logger = logger;
        }

        public bool IsEnabled => Volatile.Read(ref _disposed) == 0;

        public ICheatLogger Logger
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Volatile.Read(ref _logger);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => Volatile.Write(ref _logger, value);
        }

        public int RunningCommandCount
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
#if UNITY_WEBGL
                lock (_stateLock)
                {
                    return _commandStates.Count;
                }
#else
                return _commandStates.Count;
#endif
            }
        }

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
                    Interlocked.Read(ref _faultedCommandCount));
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
                Logger?.LogError(string.Concat(ErrClassArgPrefix, commandId, ErrClassArgSuffix));
                return UniTask.CompletedTask;
            }

            return PublishAsync(new CheatCommandClass<T>(commandId, arg), new CheatCommandExecutionOptions(router));
        }

        public async UniTask PublishAsync<TCommand>(TCommand command, CheatCommandExecutionOptions options = default)
            where TCommand : ICheatCommand
        {
            ThrowIfDisposed();

            if (!ValidateCommandId(command.CommandId))
            {
                return;
            }

            Router targetRouter = options.Router ?? Router.Default;
            long sequence = options.DuplicatePolicy == CheatDuplicatePolicy.AllowParallel
                ? Interlocked.Increment(ref _parallelSequence)
                : 0;
            var key = new CommandStateKey(command.CommandId, targetRouter, typeof(TCommand).TypeHandle, sequence);
            var state = new CommandExecutionState();

            if (!TryRegisterState(key, state))
            {
                state.Dispose();
                Interlocked.Increment(ref _droppedDuplicateCount);
                return;
            }

            Interlocked.Increment(ref _publishedCommandCount);

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
                Logger?.LogException(exception);
            }
            finally
            {
                TryRemoveState(key, state);
                state.Dispose();
            }
        }

        public bool IsCommandRunning(string commandId)
        {
            if (string.IsNullOrEmpty(commandId))
            {
                return false;
            }

#if UNITY_WEBGL
            lock (_stateLock)
            {
                foreach (var pair in _commandStates)
                {
                    if (string.Equals(pair.Key.CommandId, commandId, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }
#else
            foreach (var pair in _commandStates)
            {
                if (string.Equals(pair.Key.CommandId, commandId, StringComparison.Ordinal))
                {
                    return true;
                }
            }
#endif

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

#if UNITY_WEBGL
            lock (_stateLock)
            {
                foreach (var pair in _commandStates)
                {
                    if (ShouldCancel(pair.Key, commandId, router))
                    {
                        if (pair.Value.Cancel(Logger))
                        {
                            Interlocked.Increment(ref _cancelRequestedCount);
                        }
                    }
                }
            }
#else
            foreach (var pair in _commandStates)
            {
                if (ShouldCancel(pair.Key, commandId, router))
                {
                    if (pair.Value.Cancel(Logger))
                    {
                        Interlocked.Increment(ref _cancelRequestedCount);
                    }
                }
            }
#endif
        }

        public void ClearAll()
        {
#if UNITY_WEBGL
            lock (_stateLock)
            {
                foreach (var pair in _commandStates)
                {
                    pair.Value.Cancel(Logger);
                }
            }
#else
            foreach (var pair in _commandStates)
            {
                pair.Value.Cancel(Logger);
            }
#endif
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

#if UNITY_WEBGL
            lock (_stateLock)
            {
                foreach (var pair in _commandStates)
                {
                    pair.Value.Cancel(Logger);
                }

                _commandStates.Clear();
            }
#else
            foreach (var pair in _commandStates)
            {
                if (TryRemoveState(pair.Key, pair.Value))
                {
                    pair.Value.Cancel(Logger);
                }
            }
#endif
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

            Logger?.LogError(ErrCommandIdNullOrEmpty);
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

        private bool TryRegisterState(CommandStateKey key, CommandExecutionState state)
        {
#if UNITY_WEBGL
            lock (_stateLock)
            {
                if (_commandStates.ContainsKey(key))
                {
                    return false;
                }

                _commandStates.Add(key, state);
                return true;
            }
#else
            return _commandStates.TryAdd(key, state);
#endif
        }

        private bool TryRemoveState(CommandStateKey key, CommandExecutionState state)
        {
#if UNITY_WEBGL
            lock (_stateLock)
            {
                if (_commandStates.TryGetValue(key, out var current) && ReferenceEquals(current, state))
                {
                    _commandStates.Remove(key);
                    return true;
                }
            }

            return false;
#else
            var pair = new KeyValuePair<CommandStateKey, CommandExecutionState>(key, state);
            return ((ICollection<KeyValuePair<CommandStateKey, CommandExecutionState>>)_commandStates).Remove(pair);
#endif
        }
    }
#else
    public sealed class CheatCommandRuntime : ICheatCommandRuntime
    {
        private ICheatLogger _logger;

        public CheatCommandRuntime(ICheatLogger logger = null)
        {
            _logger = logger;
        }

        public bool IsEnabled => false;

        public ICheatLogger Logger
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _logger;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => _logger = value;
        }

        public int RunningCommandCount => 0;

        public CheatRuntimeMetrics Metrics => default;

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
