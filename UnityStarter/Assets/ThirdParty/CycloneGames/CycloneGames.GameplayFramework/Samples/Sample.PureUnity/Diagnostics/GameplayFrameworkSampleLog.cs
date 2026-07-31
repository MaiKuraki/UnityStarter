using System;
using CycloneGames.Logging;

namespace CycloneGames.GameplayFramework.Runtime.Sample.PureUnity
{
    internal static class GameplayFrameworkSampleLog
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
