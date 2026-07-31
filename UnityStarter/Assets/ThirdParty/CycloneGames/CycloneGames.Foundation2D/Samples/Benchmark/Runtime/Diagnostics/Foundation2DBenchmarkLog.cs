using System;
using CycloneGames.Logging;

namespace CycloneGames.Foundation2D.Sample.Runtime
{
    internal static class Foundation2DBenchmarkLog
    {
        internal const string Category = "CycloneGames.Foundation2D.Benchmark";
        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(
                Category,
                logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
