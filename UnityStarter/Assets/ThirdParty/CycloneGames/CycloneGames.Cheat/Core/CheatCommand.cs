using System;
using System.Runtime.CompilerServices;

namespace CycloneGames.Cheat.Core
{
    /// <summary>
    /// Marker interface for commands that can be dispatched through the cheat runtime.
    /// </summary>
    public interface ICheatCommand : VitalRouter.ICommand
    {
        string CommandId { get; }
    }

    public readonly struct CheatCommand : ICheatCommand
    {
        public string CommandId { get; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public CheatCommand(string commandId)
        {
            CommandId = commandId ?? throw new ArgumentNullException(nameof(commandId));
        }
    }

    public readonly struct CheatCommand<T> : ICheatCommand where T : struct
    {
        public string CommandId { get; }
        public readonly T Arg;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public CheatCommand(string commandId, in T arg)
        {
            CommandId = commandId ?? throw new ArgumentNullException(nameof(commandId));
            Arg = arg;
        }
    }

    /// <summary>
    /// Class-argument command. Heap-allocated - prefer struct variants when possible.
    /// </summary>
    public sealed class CheatCommandClass<T> : ICheatCommand where T : class
    {
        public string CommandId { get; }
        public readonly T Arg;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public CheatCommandClass(string commandId, T arg)
        {
            CommandId = commandId ?? throw new ArgumentNullException(nameof(commandId));
            Arg = arg ?? throw new ArgumentNullException(nameof(arg));
        }
    }

    public readonly struct CheatCommand<T1, T2> : ICheatCommand
        where T1 : struct where T2 : struct
    {
        public string CommandId { get; }
        public readonly T1 Arg1;
        public readonly T2 Arg2;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public CheatCommand(string commandId, in T1 arg1, in T2 arg2)
        {
            CommandId = commandId ?? throw new ArgumentNullException(nameof(commandId));
            Arg1 = arg1;
            Arg2 = arg2;
        }
    }

    public readonly struct CheatCommand<T1, T2, T3> : ICheatCommand
        where T1 : struct where T2 : struct where T3 : struct
    {
        public string CommandId { get; }
        public readonly T1 Arg1;
        public readonly T2 Arg2;
        public readonly T3 Arg3;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public CheatCommand(string commandId, in T1 arg1, in T2 arg2, in T3 arg3)
        {
            CommandId = commandId ?? throw new ArgumentNullException(nameof(commandId));
            Arg1 = arg1;
            Arg2 = arg2;
            Arg3 = arg3;
        }
    }
}
