using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;
using Cysharp.Threading.Tasks;
using VitalRouter;
using UnityEngine;

#if UNITY_WEBGL
using System.Collections.Generic;
#endif

namespace CycloneGames.Cheat.Runtime
{
    /// <summary>
    /// Static utility for publishing cheat commands with built-in de-duplication and cancellation support.
    /// Optimized for zero/minimal GC allocation and cross-platform compatibility.
    /// </summary>
    public static class CheatCommandUtility
    {
#if UNITY_WEBGL
        private static readonly Dictionary<string, CancellationTokenSource> commandStates = 
            new Dictionary<string, CancellationTokenSource>(StringComparer.Ordinal);
        private static readonly Queue<CancellationTokenSource> ctsPool = new Queue<CancellationTokenSource>();
#else
        private static readonly ConcurrentDictionary<string, CancellationTokenSource> commandStates =
            new ConcurrentDictionary<string, CancellationTokenSource>(StringComparer.Ordinal);
        private static readonly ConcurrentQueue<CancellationTokenSource> ctsPool = new ConcurrentQueue<CancellationTokenSource>();
#endif

        private const int POOL_CAPACITY = 32;

        /// <summary>
        /// Optional logger interface for flexible integration. Set to null to disable logging.
        /// </summary>
        public static ICheatLogger Logger { get; set; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static CancellationTokenSource GetCts()
        {
#if UNITY_WEBGL
            if (ctsPool.Count > 0)
            {
                var cts = ctsPool.Dequeue();
                if (!cts.IsCancellationRequested)
                {
                    return cts;
                }
                cts.Dispose();
            }
#else
            if (ctsPool.TryDequeue(out var cts))
            {
                if (!cts.IsCancellationRequested)
                {
                    return cts;
                }
                cts.Dispose();
            }
#endif
            return new CancellationTokenSource();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ReturnCts(CancellationTokenSource cts)
        {
            if (cts == null) return;

            if (ctsPool.Count >= POOL_CAPACITY)
            {
                cts.Dispose();
                return;
            }

            if (!cts.IsCancellationRequested)
            {
                ctsPool.Enqueue(cts);
            }
            else
            {
                cts.Dispose();
            }
        }

        /// <summary>
        /// Publishes a zero-argument cheat command. Returns immediately if command is already running.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UniTask PublishCheatCommand(string commandId, Router router = null)
        {
            if (string.IsNullOrEmpty(commandId))
            {
                Logger?.LogError($"[CheatCommandUtility] CommandID cannot be null or empty.");
                return UniTask.CompletedTask;
            }

            var command = new CheatCommand(commandId);
            return PublishInternal(command, router);
        }

        /// <summary>
        /// Publishes a struct-argument cheat command. Zero-allocation for value types.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UniTask PublishCheatCommand<T>(string commandId, T inArg, Router router = null) where T : struct
        {
            if (string.IsNullOrEmpty(commandId))
            {
                Logger?.LogError($"[CheatCommandUtility] CommandID cannot be null or empty.");
                return UniTask.CompletedTask;
            }

            var command = new CheatCommand<T>(commandId, inArg);
            return PublishInternal(command, router);
        }

        /// <summary>
        /// Publishes a class-argument cheat command. Allocates on heap - use sparingly.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UniTask PublishCheatCommandWithClass<T>(string commandId, T inArg, Router router = null) where T : class
        {
            if (string.IsNullOrEmpty(commandId))
            {
                Logger?.LogError($"[CheatCommandUtility] CommandID cannot be null or empty.");
                return UniTask.CompletedTask;
            }

            if (inArg == null)
            {
                Logger?.LogError($"[CheatCommandUtility] Argument for command '{commandId}' cannot be null.");
                return UniTask.CompletedTask;
            }

            var command = new CheatCommandClass<T>(commandId, inArg);
            return PublishInternal(command, router);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static async UniTask PublishInternal<T>(T command, Router router) where T : ICheatCommand
        {
            var targetRouter = router ?? Router.Default;
            var cts = GetCts();

#if UNITY_WEBGL
            if (commandStates.ContainsKey(command.CommandID))
            {
                ReturnCts(cts);
                return;
            }
            commandStates[command.CommandID] = cts;
#else
            if (!commandStates.TryAdd(command.CommandID, cts))
            {
                ReturnCts(cts);
                return;
            }
#endif

            try
            {
                await targetRouter.PublishAsync(command, cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected when command is cancelled
            }
            catch (Exception ex)
            {
                Logger?.LogException(ex);
            }
            finally
            {
#if UNITY_WEBGL
                if (commandStates.TryGetValue(command.CommandID, out var existingCts) && 
                    ReferenceEquals(existingCts, cts))
                {
                    commandStates.Remove(command.CommandID);
                    if (!existingCts.IsCancellationRequested)
                    {
                        existingCts.Cancel();
                    }
                    ReturnCts(existingCts);
                }
#else
                if (commandStates.TryRemove(command.CommandID, out var existingCts))
                {
                    if (!existingCts.IsCancellationRequested)
                    {
                        existingCts.Cancel();
                    }
                    ReturnCts(existingCts);
                }
#endif
            }
        }

        /// <summary>
        /// Cancels a running command with the given commandId. Safe to call if command is not running.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void CancelCheatCommand(string commandId)
        {
            if (string.IsNullOrEmpty(commandId)) return;

#if UNITY_WEBGL
            if (commandStates.TryGetValue(commandId, out var cts))
            {
                commandStates.Remove(commandId);
                if (!cts.IsCancellationRequested)
                {
                    cts.Cancel();
                }
                ReturnCts(cts);
            }
#else
            if (commandStates.TryRemove(commandId, out var cts))
            {
                if (!cts.IsCancellationRequested)
                {
                    cts.Cancel();
                }
                ReturnCts(cts);
            }
#endif
        }

        /// <summary>
        /// Clears all running commands and resets the internal state. Use with caution.
        /// </summary>
        public static void ClearAll()
        {
#if UNITY_WEBGL
            foreach (var kvp in commandStates)
            {
                if (!kvp.Value.IsCancellationRequested)
                {
                    kvp.Value.Cancel();
                }
                kvp.Value.Dispose();
            }
            commandStates.Clear();
#else
            foreach (var kvp in commandStates)
            {
                if (!kvp.Value.IsCancellationRequested)
                {
                    kvp.Value.Cancel();
                }
                kvp.Value.Dispose();
            }
            commandStates.Clear();
#endif

            while (ctsPool.Count > 0)
            {
#if UNITY_WEBGL
                ctsPool.Dequeue()?.Dispose();
#else
                if (ctsPool.TryDequeue(out var cts))
                {
                    cts.Dispose();
                }
#endif
            }
        }
    }

    /// <summary>
    /// Optional logger interface for flexible integration. Implement to customize logging behavior.
    /// </summary>
    public interface ICheatLogger
    {
        void LogError(string message);
        void LogException(Exception exception);
    }
}