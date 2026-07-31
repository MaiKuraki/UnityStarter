using System;
using CycloneGames.Logging;

namespace CycloneGames.Networking.Unity.Runtime
{
    internal static class NetworkingUnityRuntimeLog
    {
        internal const string Category = NetworkingDiagnosticCategories.Root;
        internal const string SecurityCategory = "CycloneGames.Networking.Security";

        internal static readonly LogChannel Channel = LogChannel.Create(Category);
        internal static readonly LogChannel SecurityChannel = LogChannel.Create(SecurityCategory);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(
                Category,
                logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }

    }
}
