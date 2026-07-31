using System;
using CycloneGames.Logging;

namespace CycloneGames.Utility.Runtime
{
    internal static class UtilityLog
    {
        internal const string Category = "CycloneGames.Utility";
        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(Category, logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
