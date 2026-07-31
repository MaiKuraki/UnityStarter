using System;
using CycloneGames.Logging;

namespace CycloneGames.Networking.Adapter.Mirror
{
    internal static class NetworkingMirrorAdapterLog
    {
        internal const string Category = "CycloneGames.Networking.Adapter.Mirror";

        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(
                Category,
                logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
