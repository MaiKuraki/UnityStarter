using System;
using CycloneGames.Logging;

namespace CycloneGames.Choreography.UnityAnimation
{
    internal static class ChoreographyUnityAnimationLog
    {
        internal const string Category = "CycloneGames.Choreography.UnityAnimation";
        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(
                Category,
                logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
