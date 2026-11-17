using System;
using CycloneGames.Logging;

namespace CycloneGames.RPGFoundation.Trajectory.Editor
{
    internal static class RpgTrajectoryEditorLog
    {
        internal const string Category = "CycloneGames.RPGFoundation.Trajectory.Editor";
        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(Category, logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
