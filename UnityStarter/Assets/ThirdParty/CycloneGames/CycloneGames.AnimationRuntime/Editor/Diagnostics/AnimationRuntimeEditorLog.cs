using System;
using CycloneGames.Logging;

namespace CycloneGames.AnimationRuntime.Editor
{
    /// <summary>
    /// Editor-side logging facade for this package. The editor assembly is a separate compilation unit, so it
    /// owns its own category and channel instead of reaching into the runtime facade (which is internal by
    /// contract, see CG0050).
    /// </summary>
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
