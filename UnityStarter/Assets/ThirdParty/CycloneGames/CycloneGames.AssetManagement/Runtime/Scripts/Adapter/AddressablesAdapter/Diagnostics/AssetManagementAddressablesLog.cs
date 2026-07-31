using System;
using CycloneGames.Logging;

namespace CycloneGames.AssetManagement.Runtime
{
    internal static class AssetManagementAddressablesLog
    {
        internal const string Category = "CycloneGames.AssetManagement.Addressables";
        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(Category, logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
