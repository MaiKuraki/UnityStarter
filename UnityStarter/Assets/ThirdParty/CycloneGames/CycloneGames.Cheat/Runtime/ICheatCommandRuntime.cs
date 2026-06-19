using System;
using CycloneGames.Cheat.Core;
using Cysharp.Threading.Tasks;
using VitalRouter;

namespace CycloneGames.Cheat.Runtime
{
    public interface ICheatCommandPublisher
    {
        bool IsEnabled { get; }

        UniTask PublishAsync(string commandId, Router router = null);

        UniTask PublishAsync<T>(string commandId, T arg, Router router = null) where T : struct;

        UniTask PublishAsync<T1, T2>(string commandId, T1 arg1, T2 arg2, Router router = null)
            where T1 : struct
            where T2 : struct;

        UniTask PublishAsync<T1, T2, T3>(string commandId, T1 arg1, T2 arg2, T3 arg3, Router router = null)
            where T1 : struct
            where T2 : struct
            where T3 : struct;

        UniTask PublishClassAsync<T>(string commandId, T arg, Router router = null) where T : class;

        UniTask PublishAsync<TCommand>(TCommand command, CheatCommandExecutionOptions options = default)
            where TCommand : ICheatCommand;
    }

    public interface ICheatCommandControl
    {
        int RunningCommandCount { get; }

        CheatRuntimeMetrics Metrics { get; }

        bool IsCommandRunning(string commandId);

        void CancelCommand(string commandId);

        void CancelCommand(string commandId, Router router);

        void ClearAll();
    }

    public interface ICheatCommandRuntime : ICheatCommandPublisher, ICheatCommandControl, IDisposable
    {
        ICheatLogger Logger { get; set; }
    }
}
