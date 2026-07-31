using System;
using CycloneGames.Logging;

namespace CycloneGames.Factory
{
    internal static class FactorySamplesLog
    {
        internal const string Category = "CycloneGames.Factory.Samples";
        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(
                Category,
                logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
