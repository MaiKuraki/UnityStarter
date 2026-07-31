using System;
using CycloneGames.Logging;

namespace CycloneGames.UIFramework.Editor
{
    internal static class UIFrameworkEditorLog
    {
        internal const string Category = "CycloneGames.UIFramework.Editor";
        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(Category, logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
