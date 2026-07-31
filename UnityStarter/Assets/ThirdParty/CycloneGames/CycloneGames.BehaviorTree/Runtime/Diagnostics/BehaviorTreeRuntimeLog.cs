using System;
using CycloneGames.Logging;

namespace CycloneGames.BehaviorTree.Runtime
{
    internal static class BehaviorTreeRuntimeLog
    {
        internal const string Category = "CycloneGames.BehaviorTree";

        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(
                Category,
                logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
