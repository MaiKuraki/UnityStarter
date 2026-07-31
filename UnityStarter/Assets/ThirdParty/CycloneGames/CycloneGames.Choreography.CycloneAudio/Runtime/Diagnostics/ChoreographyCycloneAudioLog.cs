using System;
using CycloneGames.Logging;

namespace CycloneGames.Choreography.CycloneAudio
{
    internal static class ChoreographyCycloneAudioLog
    {
        internal const string Category = "CycloneGames.Choreography.CycloneAudio";
        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(
                Category,
                logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
