using System;
using CycloneGames.Logging;

namespace CycloneGames.Networking
{
    internal static class NetworkingCoreLog
    {
        internal const string Category = LogCategory.Root;

        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(
                Category,
                logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
