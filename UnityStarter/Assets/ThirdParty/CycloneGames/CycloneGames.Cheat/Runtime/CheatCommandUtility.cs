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
    public static class CheatCommandUtility
    {
#if UNITY_WEBGL
        // On WebGL (single-threaded), use non-thread-safe collections for maximum performance.
        private static readonly Dictionary<string, CancellationTokenSource> commandStates = new();
        private static readonly Queue<CancellationTokenSource> ctsPool = new();
#else
        // On multi-threaded platforms, use concurrent collections for thread safety.
        private static readonly ConcurrentDictionary<string, CancellationTokenSource> commandStates = new();
        private static readonly ConcurrentQueue<CancellationTokenSource> ctsPool = new();
#endif
        
        // A more reasonable pool size for a cheat system.
        private const int POOL_CAPACITY = 30;

        private static CancellationTokenSource GetCts()
        {
#if UNITY_WEBGL
            if (ctsPool.Count > 0)
            {
                var cts = ctsPool.Dequeue();
#else
            if (ctsPool.TryDequeue(out var cts))
            {
#endif
                if (cts.IsCancellationRequested)
                {
                    cts.Dispose();
                    return new CancellationTokenSource();
                }
                return cts;
            }
            return new CancellationTokenSource();
        }

        private static void ReturnCts(CancellationTokenSource cts)
        {
            if (cts == null || ctsPool.Count >= POOL_CAPACITY)
            {
                cts?.Dispose();
                return;
            }
            ctsPool.Enqueue(cts);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UniTask PublishCheatCommand(string commandId, Router router = null)
        {
            var command = new CheatCommand(commandId);
            return PublishInternal(command, router);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UniTask PublishCheatCommand<T>(string commandId, T inArg, Router router = null) where T : struct
        {
            var command = new CheatCommand<T>(commandId, inArg);
            return PublishInternal(command, router);
        }

        // Use a distinct method name for class types to avoid any overload ambiguity.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static UniTask PublishCheatCommandWithClass<T>(string commandId, T inArg, Router router = null) where T : class
        {
            if (inArg == null)
            {
                Debug.LogError($"[CheatCommandUtility] Argument for command '{commandId}' cannot be null.");
                return UniTask.CompletedTask;
            }
            var command = new CheatCommandClass<T>(commandId, inArg);
            return PublishInternal(command, router);
        }

        private static async UniTask PublishInternal<T>(T command, Router router) where T : ICheatCommand
        {
            var targetRouter = router ?? Router.Default;
            var cts = GetCts();
            
#if UNITY_WEBGL
            if (!commandStates.ContainsKey(command.CommandID))
            {
                commandStates.Add(command.CommandID, cts);
#else
            if (!commandStates.TryAdd(command.CommandID, cts))
            {
#endif
                ReturnCts(cts);
                return;
            }

            try
            {
                await targetRouter.PublishAsync(command, cts.Token);
            }
            catch (OperationCanceledException) { /* Expected. */ }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
            finally
            {
#if UNITY_WEBGL
                if (commandStates.TryGetValue(command.CommandID, out var existingCts))
                {
                    commandStates.Remove(command.CommandID);
#else
                if (commandStates.TryRemove(command.CommandID, out var existingCts))
                {
#endif
                    if (!existingCts.IsCancellationRequested)
                    {
                        existingCts.Cancel();
                    }
                    ReturnCts(existingCts);
                }
            }
        }

        public static void CancelCheatCommand(string commandId)
        {
#if UNITY_WEBGL
            if (commandStates.TryGetValue(commandId, out var cts))
            {
                commandStates.Remove(commandId);
#else
            if (commandStates.TryRemove(commandId, out var cts))
            {
#endif
                if (!cts.IsCancellationRequested)
                {
                    cts.Cancel();
                }
                ReturnCts(cts);
            }
        }
    }
}
