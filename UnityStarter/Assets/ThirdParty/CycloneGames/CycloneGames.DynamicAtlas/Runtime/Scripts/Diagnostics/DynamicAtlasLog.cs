using System;
using CycloneGames.Logging;

namespace CycloneGames.DynamicAtlas
{
    /// <summary>Package log channel; the category is stable for log filtering.</summary>
    internal static class DynamicAtlasLog
    {
        internal const string Category = "CycloneGames.DynamicAtlas";
        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(Category, logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
