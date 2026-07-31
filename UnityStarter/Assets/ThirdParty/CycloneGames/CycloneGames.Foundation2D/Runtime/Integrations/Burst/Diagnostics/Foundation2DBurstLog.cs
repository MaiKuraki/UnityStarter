using System;
using CycloneGames.Logging;

namespace CycloneGames.Foundation2D.Runtime
{
    internal static class Foundation2DBurstLog
    {
        internal const string Category = "CycloneGames.Foundation2D.Burst";
        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(Category, logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
