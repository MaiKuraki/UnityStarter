using System;
using CycloneGames.Logging;

namespace CycloneGames.AIPerception.Runtime
{
    internal static class AIPerceptionRuntimeLog
    {
        internal const string Category = "CycloneGames.AIPerception";

        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(
                Category,
                logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
