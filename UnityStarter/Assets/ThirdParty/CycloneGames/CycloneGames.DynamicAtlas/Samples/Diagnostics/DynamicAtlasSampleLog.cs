using System;
using CycloneGames.Logging;

namespace CycloneGames.DynamicAtlas
{
    /// <summary>Sample log channel; same category as the runtime package.</summary>
    internal static class DynamicAtlasSampleLog
    {
        internal const string Category = "CycloneGames.DynamicAtlas";
        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(
                Category,
                logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
