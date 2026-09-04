using System;
using System.Text;
using CycloneGames.Logging;

namespace CycloneGames.GameplayTags.Unity.Runtime
{
    /// <summary>
    /// The assembly log facade for the GameplayTags Unity runtime. All direct Unity logging from this
    /// assembly goes through this channel so the project has one place to route, filter, or redirect it.
    /// </summary>
    internal static class GameplayTagsRuntimeLog
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
