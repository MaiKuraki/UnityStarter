using System;
using CycloneGames.Logging;

namespace CycloneGames.Choreography.Audio
{
    internal static class ChoreographyAudioLog
    {
        internal const string Category = "CycloneGames.Choreography.Audio";
        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(
                Category,
                logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
