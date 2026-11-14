using System;
using CycloneGames.Logging;

namespace CycloneGames.GameplayFramework.Sample.StrangeIoC
{
    internal static class StrangeIoCSampleLog
    {
        internal const string Category = "CycloneGames.GameplayFramework.Sample.StrangeIoC";
        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(
                Category,
                logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
