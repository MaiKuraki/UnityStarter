using System;
using CycloneGames.Logging;

namespace CycloneGames.GameplayFramework.Runtime
{
    internal static class GameplayFrameworkLog
    {
        internal const string Category = "CycloneGames.GameplayFramework";

        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(
                Category,
                logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
