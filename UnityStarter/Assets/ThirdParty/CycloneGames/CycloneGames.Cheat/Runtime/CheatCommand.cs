using System;
using System.Runtime.CompilerServices;

namespace CycloneGames.Cheat.Runtime
{
    /// <summary>
    /// Base interface for all cheat commands. Commands are dispatched via VitalRouter.
    /// </summary>
    public interface ICheatCommand : VitalRouter.ICommand
    {
        /// <summary>
        /// Unique command identifier. Used for de-duplication and cancellation.
        /// </summary>
        string CommandID { get; }
    }

    /// <summary>
    /// Zero-argument cheat command.
    /// </summary>
    public readonly struct CheatCommand : ICheatCommand
    {
        public string CommandID { get; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public CheatCommand(string inCommandId)
        {
            CommandID = inCommandId ?? throw new ArgumentNullException(nameof(inCommandId));
        }
    }

    /// <summary>
    /// Struct-argument cheat command.
    /// </summary>
    public readonly struct CheatCommand<T> : ICheatCommand where T : struct
    {
        public string CommandID { get; }
        public readonly T Arg;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public CheatCommand(string inCommandId, in T arg)
        {
            CommandID = inCommandId ?? throw new ArgumentNullException(nameof(inCommandId));
            Arg = arg;
        }
    }

    /// <summary>
    /// Class-argument cheat command. Allocates on heap but provides reference semantics.
    /// </summary>
    public sealed class CheatCommandClass<T> : ICheatCommand where T : class
    {
        public string CommandID { get; }
        public readonly T Arg;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public CheatCommandClass(string inCommandId, T arg)
        {
            CommandID = inCommandId ?? throw new ArgumentNullException(nameof(inCommandId));
            Arg = arg ?? throw new ArgumentNullException(nameof(arg));
        }
    }

    /// <summary>
    /// Two struct-argument cheat command.
    /// </summary>
    public readonly struct CheatCommand<T1, T2> : ICheatCommand
        where T1 : struct where T2 : struct
    {
        public string CommandID { get; }
        public readonly T1 Arg1;
        public readonly T2 Arg2;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public CheatCommand(string inCommandId, in T1 arg1, in T2 arg2)
        {
            CommandID = inCommandId ?? throw new ArgumentNullException(nameof(inCommandId));
            Arg1 = arg1;
            Arg2 = arg2;
        }
    }

    /// <summary>
    /// Three struct-argument cheat command.
    /// </summary>
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
            CommandID = inCommandId ?? throw new ArgumentNullException(nameof(inCommandId));
            Arg1 = arg1;
            Arg2 = arg2;
            Arg3 = arg3;
        }
    }
}