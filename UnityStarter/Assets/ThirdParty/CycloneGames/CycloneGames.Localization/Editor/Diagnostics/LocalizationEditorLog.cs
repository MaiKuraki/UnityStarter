using System;
using CycloneGames.Logging;

namespace CycloneGames.Localization.Editor
{
    internal static class LocalizationEditorLog
    {
        internal const string Category = "CycloneGames.Localization.Editor";
        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(Category, logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
