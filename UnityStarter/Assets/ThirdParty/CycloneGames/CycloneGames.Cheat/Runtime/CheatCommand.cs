using System;
using System.Runtime.CompilerServices;

namespace CycloneGames.Cheat
{
    public interface ICheatCommand : VitalRouter.ICommand
    {
        string CommandID { get; }
    }

    public readonly struct CheatCommand : ICheatCommand
    {
        public string CommandID { get; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public CheatCommand(string inCommandId)
        {
            CommandID = inCommandId;
        }
    }

    public readonly struct CheatCommand<T> : ICheatCommand where T : struct
    {
        public string CommandID { get; }
        public readonly T Arg;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public CheatCommand(string inCommandId, in T arg)
        {
            CommandID = inCommandId;
            Arg = arg;
        }
    }

    public sealed class CheatCommandClass<T> : ICheatCommand where T : class
    {
        public string CommandID { get; }
        public readonly T Arg;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public CheatCommandClass(string inCommandId, T arg)
        {
            CommandID = inCommandId;
            Arg = arg ?? throw new ArgumentNullException(nameof(arg));
        }
    }

    public readonly struct CheatCommand<T1, T2> : ICheatCommand
        where T1 : struct where T2 : struct
    {
        public string CommandID { get; }
        public readonly T1 Arg1;
        public readonly T2 Arg2;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public CheatCommand(string inCommandId, in T1 arg1, in T2 arg2)
        {
            CommandID = inCommandId;
            Arg1 = arg1;
            Arg2 = arg2;
        }
    }

    public readonly struct CheatCommand<T1, T2, T3> : ICheatCommand
        where T1 : struct where T2 : struct where T3 : struct
    {
        public string CommandID { get; }
        public readonly T1 Arg1;
        public readonly T2 Arg2;
        public readonly T3 Arg3;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public CheatCommand(string inCommandId, in T1 arg1, in T2 arg2, in T3 arg3)
        {
            CommandID = inCommandId;
            Arg1 = arg1;
            Arg2 = arg2;
            Arg3 = arg3;
        }
    }
}