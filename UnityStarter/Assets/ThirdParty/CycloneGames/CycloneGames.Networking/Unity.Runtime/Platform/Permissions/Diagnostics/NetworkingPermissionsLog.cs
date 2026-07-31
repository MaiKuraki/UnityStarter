using System;
using CycloneGames.Logging;

namespace CycloneGames.Networking.Platform
{
    internal static class NetworkingPermissionsLog
    {
        internal const string Category = "CycloneGames.Networking.Platform.Permissions";

        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(
                Category,
                logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
