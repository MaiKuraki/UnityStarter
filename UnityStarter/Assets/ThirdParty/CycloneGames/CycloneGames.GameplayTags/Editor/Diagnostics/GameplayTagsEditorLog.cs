using System;
using CycloneGames.Logging;

namespace CycloneGames.GameplayTags.Unity.Editor
{
    internal static class GameplayTagsEditorLog
    {
        internal const string Category = "CycloneGames.GameplayTags";

        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(
                Category,
                logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
