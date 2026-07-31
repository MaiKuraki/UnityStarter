using System;
using CycloneGames.Logging;

namespace CycloneGames.AssetManagement.Editor
{
    internal static class AssetManagementEditorLog
    {
        internal const string Category = "CycloneGames.AssetManagement.Editor";
        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(Category, logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
