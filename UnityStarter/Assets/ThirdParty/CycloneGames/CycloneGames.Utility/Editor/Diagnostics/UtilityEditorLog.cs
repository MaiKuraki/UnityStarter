using System;
using CycloneGames.Logging;

namespace CycloneGames.Utility.Editor
{
    internal static class UtilityEditorLog
    {
        internal const string Category = "CycloneGames.Utility.Editor";
        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(Category, logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
