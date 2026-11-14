using System;
using CycloneGames.Logging;

namespace CycloneGames.GameplayFramework.Sample.Zenject
{
    internal static class ZenjectSampleLog
    {
        internal const string Category = "CycloneGames.GameplayFramework.Sample.Zenject";
        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(
                Category,
                logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
