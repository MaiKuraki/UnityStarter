using System;
using CycloneGames.Logging;

namespace CycloneGames.Cheat.Runtime
{
    internal static class CheatRuntimeLog
    {
        internal const string Category = "CycloneGames.Cheat";
        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(
                Category,
                logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }

        internal static LogChannel CreateOptional(ILogWriter logWriter)
        {
            return logWriter == null ? Channel : Create(logWriter);
        }
    }
}
