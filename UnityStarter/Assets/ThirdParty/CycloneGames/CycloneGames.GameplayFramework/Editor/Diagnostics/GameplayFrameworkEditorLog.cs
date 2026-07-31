using System;
using CycloneGames.Logging;

namespace CycloneGames.GameplayFramework.Runtime.Editor
{
    internal static class GameplayFrameworkEditorLog
    {
        internal const string Category = "CycloneGames.GameplayFramework.Editor";

        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(
                Category,
                logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
