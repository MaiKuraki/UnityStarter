using System;
using CycloneGames.Logging;

namespace CycloneGames.RPGFoundation.Interaction.Runtime
{
    internal static class RpgInteractionLog
    {
        internal const string Category = "CycloneGames.RPGFoundation.Interaction";
        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(Category, logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
