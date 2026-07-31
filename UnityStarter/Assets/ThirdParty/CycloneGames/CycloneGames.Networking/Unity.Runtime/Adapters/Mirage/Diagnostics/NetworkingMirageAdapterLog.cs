using System;
using CycloneGames.Logging;

namespace CycloneGames.Networking.Adapter.Mirage
{
    internal static class NetworkingMirageAdapterLog
    {
        internal const string Category = "CycloneGames.Networking.Adapter.Mirage";

        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(
                Category,
                logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
