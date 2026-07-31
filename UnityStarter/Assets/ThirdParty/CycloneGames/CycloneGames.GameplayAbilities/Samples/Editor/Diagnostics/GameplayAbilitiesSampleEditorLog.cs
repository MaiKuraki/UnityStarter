using System;
using CycloneGames.Logging;

namespace CycloneGames.GameplayAbilities.Sample.Editor
{
    internal static class GameplayAbilitiesSampleEditorLog
    {
        internal const string Category = "CycloneGames.GameplayAbilities";
        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(
                Category,
                logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
