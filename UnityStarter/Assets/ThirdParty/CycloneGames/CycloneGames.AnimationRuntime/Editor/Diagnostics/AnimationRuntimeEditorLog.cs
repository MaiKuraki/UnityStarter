using System;
using CycloneGames.Logging;

namespace CycloneGames.AnimationRuntime.Editor
{
    /// <summary>Logging channel for the Editor assembly.</summary>
    internal static class AnimationRuntimeEditorLog
    {
        internal const string Category = "CycloneGames.AnimationRuntime.Editor";

        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(
                Category,
                logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
