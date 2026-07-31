using System;
using CycloneGames.Logging;

namespace CycloneGames.GameplayTags.Core
{
    internal static class GameplayTagsCoreLog
    {
        internal const string Category = "CycloneGames.GameplayTags";

        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(
                Category,
                logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
