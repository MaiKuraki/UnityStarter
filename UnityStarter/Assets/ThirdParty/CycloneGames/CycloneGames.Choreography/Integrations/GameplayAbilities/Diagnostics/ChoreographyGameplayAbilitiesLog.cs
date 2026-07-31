using System;
using CycloneGames.Logging;

namespace CycloneGames.Choreography.GameplayAbilities
{
    internal static class ChoreographyGameplayAbilitiesLog
    {
        internal const string Category = "CycloneGames.Choreography.GameplayAbilities";
        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(
                Category,
                logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
