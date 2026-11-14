using System;
using CycloneGames.Logging;

namespace CycloneGames.GameplayFramework.Sample.VContainer
{
    internal static class VContainerSampleLog
    {
        internal const string Category = "CycloneGames.GameplayFramework.Sample.VContainer";
        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(
                Category,
                logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
