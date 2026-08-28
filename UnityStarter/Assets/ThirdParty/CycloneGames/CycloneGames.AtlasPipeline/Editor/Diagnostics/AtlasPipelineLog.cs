using System;
using CycloneGames.Logging;

namespace CycloneGames.AtlasPipeline
{
    /// <summary>
    /// Module-level log facade. Follows the CycloneGames.Logging convention: each package owns its
    /// Category and LogChannel, and the backend writer is installed by the CycloneGames.Logging.Unity
    /// editor/runtime bootstrap.
    /// </summary>
    internal static class AtlasPipelineLog
    {
        internal const string Category = "CycloneGames.AtlasPipeline";

        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(
                Category,
                logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
