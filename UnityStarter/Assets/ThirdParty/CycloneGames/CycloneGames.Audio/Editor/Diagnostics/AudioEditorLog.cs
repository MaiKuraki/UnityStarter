using System;
using CycloneGames.Logging;

namespace CycloneGames.Audio.Editor
{
    internal static class AudioEditorLog
    {
        internal const string Category = "CycloneGames.Audio.Editor";

        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(
                Category,
                logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
