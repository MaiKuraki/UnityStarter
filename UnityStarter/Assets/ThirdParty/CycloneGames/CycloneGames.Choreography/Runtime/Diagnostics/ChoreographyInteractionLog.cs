using System;
using CycloneGames.Logging;

namespace CycloneGames.Choreography
{
    internal static class ChoreographyInteractionLog
    {
        internal const string Category = "CycloneGames.Choreography";
        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(
                Category,
                logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
