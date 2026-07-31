using System;
using CycloneGames.Logging;

namespace CycloneGames.RPGFoundation.Movement.Integrations.AStar
{
    internal static class RpgAStarLog
    {
        internal const string Category = "CycloneGames.RPGFoundation.Movement.AStar";
        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(Category, logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
