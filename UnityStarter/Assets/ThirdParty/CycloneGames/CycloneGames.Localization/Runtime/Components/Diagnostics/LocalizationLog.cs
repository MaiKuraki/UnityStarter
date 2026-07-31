using System;
using CycloneGames.Logging;

namespace CycloneGames.Localization.Runtime
{
    internal static class LocalizationLog
    {
        internal const string Category = "CycloneGames.Localization";
        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(Category, logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
