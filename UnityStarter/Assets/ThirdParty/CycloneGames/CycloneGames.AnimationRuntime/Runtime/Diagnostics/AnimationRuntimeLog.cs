using System;
using CycloneGames.Logging;

namespace CycloneGames.AnimationRuntime
{
    /// <summary>
    /// Category-bound logging entry point for this package.
    /// The fallback writer is resolved per call by <see cref="LogChannel"/>, so a host can swap the backend at
    /// runtime; an explicit <see cref="ILogWriter"/> can be injected through <see cref="Create"/>.
    /// </summary>
    internal static class AnimationRuntimeLog
    {
        internal const string Category = "CycloneGames.AnimationRuntime";

        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(
                Category,
                logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
