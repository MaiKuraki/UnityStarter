using System;
using CycloneGames.Logging;

namespace CycloneGames.BehaviorTree.Editor
{
    internal static class BehaviorTreeEditorLog
    {
        internal const string Category = "CycloneGames.BehaviorTree.Editor";

        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(
                Category,
                logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
