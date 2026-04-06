using System;
using System.Runtime.CompilerServices;
using Cysharp.Threading.Tasks;
using VitalRouter;
#if ENABLE_CHEAT
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
#endif

namespace CycloneGames.Cheat.Runtime
{
    public interface ICheatLogger
    {
        void LogError(string message);
        void LogException(Exception exception);
    }

#if ENABLE_CHEAT

    /// <summary>
    /// Cheat command dispatcher with de-duplication and cancellation.
    /// Compile-gated by ENABLE_CHEAT — when undefined, all methods become no-op stubs that IL2CPP inlines to zero cost.
    /// Call sites never need #if guards.
    /// </summary>
    public static class CheatCommandUtility
    {
        private readonly struct CommandStateKey : IEquatable<CommandStateKey>
        {
            public readonly string CommandId;
            public readonly Router Router;
            public readonly RuntimeTypeHandle CommandTypeHandle;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public CommandStateKey(string commandId, Router router, RuntimeTypeHandle commandTypeHandle)
            {
                CommandId = commandId;
                Router = router;
                CommandTypeHandle = commandTypeHandle;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool Equals(CommandStateKey other)
            {
                return string.Equals(CommandId, other.CommandId, StringComparison.Ordinal)
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
                    var hash = 17;
                    hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(CommandId);
                    hash = (hash * 31) + (Router != null ? RuntimeHelpers.GetHashCode(Router) : 0);
                    hash = (hash * 31) + CommandTypeHandle.GetHashCode();
                    return hash;
                }
            }
        }

        private const string ErrCommandIdNullOrEmpty = "[CheatCommandUtility] CommandID cannot be null or empty.";
        private const string ErrPrefix = "[CheatCommandUtility] Argument for command '";
        private const string ErrSuffix = "' cannot be null.";

#if UNITY_WEBGL
        private static readonly Dictionary<CommandStateKey, CancellationTokenSource> commandStates =
            new Dictionary<CommandStateKey, CancellationTokenSource>();
        private static readonly object webGlLock = new object();
        private static readonly List<CommandStateKey> s_cancelBuffer = new List<CommandStateKey>(16);
#else
        private static readonly ConcurrentDictionary<CommandStateKey, CancellationTokenSource> commandStates =
            new ConcurrentDictionary<CommandStateKey, CancellationTokenSource>();
#endif

        private static ICheatLogger s_logger = new UnityDebugCheatLogger();

        public static ICheatLogger Logger
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Volatile.Read(ref s_logger);
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => Volatile.Write(ref s_logger, value);
        }

        public static int RunningCommandCount
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
#if UNITY_WEBGL
                lock (webGlLock) { return commandStates.Count; }
#else
                return commandStates.Count;
#endif
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void CancelAndDispose(CancellationTokenSource cts)
        {
            if (cts == null) return;

            if (!cts.IsCancellationRequested)
            {
                cts.Cancel();
            }

            cts.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UniTask PublishCheatCommand(string commandId, Router router = null)
        {
            if (string.IsNullOrEmpty(commandId))
            {
                Logger?.LogError(ErrCommandIdNullOrEmpty);
                return UniTask.CompletedTask;
            }

            var command = new CheatCommand(commandId);
            return PublishInternal(command, router);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UniTask PublishCheatCommand<T>(string commandId, T inArg, Router router = null) where T : struct
        {
            if (string.IsNullOrEmpty(commandId))
            {
                Logger?.LogError(ErrCommandIdNullOrEmpty);
                return UniTask.CompletedTask;
            }

            var command = new CheatCommand<T>(commandId, inArg);
            return PublishInternal(command, router);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UniTask PublishCheatCommand<T1, T2>(
            string commandId,
            T1 inArg1,
            T2 inArg2,
            Router router = null)
            where T1 : struct
            where T2 : struct
        {
            if (string.IsNullOrEmpty(commandId))
            {
                Logger?.LogError(ErrCommandIdNullOrEmpty);
                return UniTask.CompletedTask;
            }

            var command = new CheatCommand<T1, T2>(commandId, inArg1, inArg2);
            return PublishInternal(command, router);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UniTask PublishCheatCommand<T1, T2, T3>(
            string commandId,
            T1 inArg1,
            T2 inArg2,
            T3 inArg3,
            Router router = null)
            where T1 : struct
            where T2 : struct
            where T3 : struct
        {
            if (string.IsNullOrEmpty(commandId))
            {
                Logger?.LogError(ErrCommandIdNullOrEmpty);
                return UniTask.CompletedTask;
            }

            var command = new CheatCommand<T1, T2, T3>(commandId, inArg1, inArg2, inArg3);
            return PublishInternal(command, router);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UniTask PublishCheatCommandWithClass<T>(string commandId, T inArg, Router router = null) where T : class
        {
            if (string.IsNullOrEmpty(commandId))
            {
                Logger?.LogError(ErrCommandIdNullOrEmpty);
                return UniTask.CompletedTask;
            }

            if (inArg == null)
            {
                Logger?.LogError(string.Concat(ErrPrefix, commandId, ErrSuffix));
                return UniTask.CompletedTask;
            }

            var command = new CheatCommandClass<T>(commandId, inArg);
            return PublishInternal(command, router);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsCommandRunning(string commandId)
        {
            if (string.IsNullOrEmpty(commandId)) return false;

#if UNITY_WEBGL
            lock (webGlLock)
            {
                foreach (var kvp in commandStates)
                {
                    if (string.Equals(kvp.Key.CommandId, commandId, StringComparison.Ordinal))
                        return true;
                }
                return false;
            }
#else
            foreach (var kvp in commandStates)
            {
                if (string.Equals(kvp.Key.CommandId, commandId, StringComparison.Ordinal))
                    return true;
            }
            return false;
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static async UniTask PublishInternal<T>(T command, Router router) where T : ICheatCommand
        {
            var targetRouter = router ?? Router.Default;
            var cts = new CancellationTokenSource();
            var key = new CommandStateKey(command.CommandID, targetRouter, typeof(T).TypeHandle);

#if UNITY_WEBGL
            lock (webGlLock)
            {
                if (commandStates.ContainsKey(key))
                {
                    CancelAndDispose(cts);
                    return;
                }

                commandStates[key] = cts;
            }
#else
            if (!commandStates.TryAdd(key, cts))
            {
                CancelAndDispose(cts);
                return;
            }
#endif

            try
            {
                await targetRouter.PublishAsync(command, cts.Token);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Logger?.LogException(ex);
            }
            finally
            {
#if UNITY_WEBGL
                CancellationTokenSource existingCts = null;
                lock (webGlLock)
                {
                    if (commandStates.TryGetValue(key, out var registeredCts) && ReferenceEquals(registeredCts, cts))
                    {
                        existingCts = registeredCts;
                        commandStates.Remove(key);
                    }
                }

                if (existingCts != null)
                {
                    CancelAndDispose(existingCts);
                }
#else
                if (commandStates.TryRemove(key, out var existingCts))
                {
                    CancelAndDispose(existingCts);
                }
#endif
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CancelCheatCommand(string commandId)
        {
            if (string.IsNullOrEmpty(commandId)) return;

            CancelCheatCommand(commandId, null);
        }

        /// <summary>
        /// Cancels all running commands matching commandId. If router is null, cancels across all routers.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CancelCheatCommand(string commandId, Router router)
        {
            if (string.IsNullOrEmpty(commandId)) return;

#if UNITY_WEBGL
            lock (webGlLock)
            {
                s_cancelBuffer.Clear();

                foreach (var kvp in commandStates)
                {
                    if (!string.Equals(kvp.Key.CommandId, commandId, StringComparison.Ordinal))
                        continue;
                    if (router != null && !ReferenceEquals(kvp.Key.Router, router))
                        continue;

                    s_cancelBuffer.Add(kvp.Key);
                }

                for (int i = 0; i < s_cancelBuffer.Count; i++)
                {
                    var key = s_cancelBuffer[i];
                    if (commandStates.TryGetValue(key, out var cts))
                    {
                        commandStates.Remove(key);
                        CancelAndDispose(cts);
                    }
                }

                s_cancelBuffer.Clear();
            }
#else
            foreach (var kvp in commandStates)
            {
                if (!string.Equals(kvp.Key.CommandId, commandId, StringComparison.Ordinal))
                    continue;
                if (router != null && !ReferenceEquals(kvp.Key.Router, router))
                    continue;

                if (commandStates.TryRemove(kvp.Key, out var cts))
                {
                    CancelAndDispose(cts);
                }
            }
#endif
        }

        /// <summary>
        /// Cancels and disposes all running commands. Use with caution (e.g. scene teardown).
        /// </summary>
        public static void ClearAll()
        {
#if UNITY_WEBGL
            lock (webGlLock)
            {
                foreach (var kvp in commandStates)
                {
                    CancelAndDispose(kvp.Value);
                }

                commandStates.Clear();
            }
#else
            foreach (var kvp in commandStates)
            {
                if (commandStates.TryRemove(kvp.Key, out var cts))
                {
                    CancelAndDispose(cts);
                }
            }

            commandStates.Clear();
#endif
        }
    }

#else // !ENABLE_CHEAT

    /// <summary>
    /// No-op stub when ENABLE_CHEAT is not defined. All methods inline to nothing under IL2CPP.
    /// Call sites compile without #if guards — API surface is identical to the active implementation.
    /// </summary>
    public static class CheatCommandUtility
    {
        private static ICheatLogger s_logger;

        public static ICheatLogger Logger
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => s_logger;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            set => s_logger = value;
        }

        public static int RunningCommandCount
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UniTask PublishCheatCommand(string commandId, Router router = null)
            => UniTask.CompletedTask;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UniTask PublishCheatCommand<T>(string commandId, T inArg, Router router = null) where T : struct
            => UniTask.CompletedTask;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UniTask PublishCheatCommand<T1, T2>(string commandId, T1 inArg1, T2 inArg2, Router router = null)
            where T1 : struct where T2 : struct
            => UniTask.CompletedTask;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UniTask PublishCheatCommand<T1, T2, T3>(string commandId, T1 inArg1, T2 inArg2, T3 inArg3, Router router = null)
            where T1 : struct where T2 : struct where T3 : struct
            => UniTask.CompletedTask;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UniTask PublishCheatCommandWithClass<T>(string commandId, T inArg, Router router = null) where T : class
            => UniTask.CompletedTask;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsCommandRunning(string commandId) => false;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CancelCheatCommand(string commandId) { }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CancelCheatCommand(string commandId, Router router) { }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ClearAll() { }
    }

#endif
}