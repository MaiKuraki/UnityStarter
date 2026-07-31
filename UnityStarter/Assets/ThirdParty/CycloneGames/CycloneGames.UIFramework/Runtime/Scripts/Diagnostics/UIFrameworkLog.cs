using System;
using CycloneGames.Logging;

namespace CycloneGames.UIFramework
{
    internal static class UIFrameworkLog
    {
        internal const string Category = "CycloneGames.UIFramework";
        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(Category, logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
