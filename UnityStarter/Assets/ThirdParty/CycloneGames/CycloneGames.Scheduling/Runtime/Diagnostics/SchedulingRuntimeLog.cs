using System;
using CycloneGames.Logging;

namespace CycloneGames.Scheduling.Runtime
{
    /// <summary>
    /// Assembly log facade. The scheduler itself never logs: only the Unity host reports, and only
    /// for terminal callbacks that throw while it is draining on shutdown.
    /// </summary>
    internal static class SchedulingRuntimeLog
    {
        internal const string Category = "CycloneGames.Scheduling";

        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(
                Category,
                logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
