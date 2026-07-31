using System;
using CycloneGames.Logging;

namespace CycloneGames.AssetManagement.Runtime.CacheRetention
{
    internal static class AssetCacheRetentionLog
    {
        internal const string Category = "CycloneGames.AssetManagement.CacheRetention";
        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(Category, logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
