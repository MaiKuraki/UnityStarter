using System;
using CycloneGames.Logging;

namespace CycloneGames.UIFramework.Runtime.Integrations.Localization
{
    internal static class UIFrameworkLocalizationLog
    {
        internal const string Category = "CycloneGames.UIFramework.Localization";

        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(Category, logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
