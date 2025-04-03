using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace CycloneGames.Cheat
{
    public static class CheatCommandUtility
    {
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _commandExecutionStatus = new();
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, CancellationTokenSource> _commandCancellationSources = new();

        public static async UniTask PublishCheatCommand(string commandId, string[] commandArgs)
        {
            // check if command is running 
            if (_commandExecutionStatus.TryGetValue(commandId, out var isRunning) && isRunning)
                return;

            // setup command execution status 
            if (!_commandExecutionStatus.TryAdd(commandId, true))
                return;

            // init CancellationTokenSource 
            var cts = new CancellationTokenSource();
            if (!_commandCancellationSources.TryAdd(commandId, cts))
            {
                _commandExecutionStatus.TryRemove(commandId, out _);
                cts.Dispose();
                return;
            }

            try
            {
                await VitalRouter.Router.Default.PublishAsync(new CheatCommand(commandId, commandArgs), cts.Token);
            }
            catch (OperationCanceledException)
            {

            }
            catch
            {

            }
            finally
            {
                // Release resources
                if (_commandCancellationSources.TryRemove(commandId, out cts))
                {
                    cts.Cancel();
                    cts.Dispose();
                }
                _commandExecutionStatus.TryRemove(commandId, out _);
            }
        }

        public static void CancelCheatCommand(string commandId)
        {
            if (_commandCancellationSources.TryRemove(commandId, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
                _commandExecutionStatus.TryRemove(commandId, out _);
            }
        }
    }
}