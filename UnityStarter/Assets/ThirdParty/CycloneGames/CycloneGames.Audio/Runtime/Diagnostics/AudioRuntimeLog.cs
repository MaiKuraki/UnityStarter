using System;
using CycloneGames.Logging;

namespace CycloneGames.Audio.Runtime
{
    internal static class AudioRuntimeLog
    {
        internal const string Category = "CycloneGames.Audio";

        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(
                Category,
                logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
