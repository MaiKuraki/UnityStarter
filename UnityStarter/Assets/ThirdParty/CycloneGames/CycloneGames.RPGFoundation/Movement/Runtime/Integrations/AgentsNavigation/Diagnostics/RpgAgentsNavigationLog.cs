using System;
using CycloneGames.Logging;

namespace CycloneGames.RPGFoundation.Movement.Integrations.AgentsNavigation
{
    internal static class RpgAgentsNavigationLog
    {
        internal const string Category = "CycloneGames.RPGFoundation.Movement.AgentsNavigation";
        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(Category, logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
