using System;
using CycloneGames.Logging;

namespace CycloneGames.RPGFoundation.Movement.Runtime
{
    internal static class RpgMovementLog
    {
        internal const string Category = "CycloneGames.RPGFoundation.Movement";
        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(Category, logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
