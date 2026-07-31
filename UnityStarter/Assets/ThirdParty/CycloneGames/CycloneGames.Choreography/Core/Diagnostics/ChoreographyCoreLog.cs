using System;
using CycloneGames.Logging;

namespace CycloneGames.Choreography.Core
{
    internal static class ChoreographyCoreLog
    {
        internal const string Category = "CycloneGames.Choreography";
        internal const string PreloadCategory = "CycloneGames.Choreography.Preload";

        internal static readonly LogChannel Channel = LogChannel.Create(Category);
        internal static readonly LogChannel PreloadChannel = LogChannel.Create(PreloadCategory);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(
                Category,
                logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }

        internal static LogChannel CreatePreload(ILogWriter logWriter)
        {
            return LogChannel.Create(
                PreloadCategory,
                logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
