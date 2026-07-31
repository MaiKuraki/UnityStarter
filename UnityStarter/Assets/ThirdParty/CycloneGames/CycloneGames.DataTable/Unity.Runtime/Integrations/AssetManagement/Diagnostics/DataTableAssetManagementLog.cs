using System;
using CycloneGames.Logging;

namespace CycloneGames.DataTable.Unity.Integrations.AssetManagement
{
    internal static class DataTableAssetManagementLog
    {
        internal const string Category = "CycloneGames.DataTable.AssetManagement";
        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(
                Category,
                logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
