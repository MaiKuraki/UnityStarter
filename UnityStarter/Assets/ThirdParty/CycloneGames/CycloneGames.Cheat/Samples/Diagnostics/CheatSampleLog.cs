using System;
using CycloneGames.Logging;

namespace CycloneGames.Cheat.Sample
{
    internal static class CheatSampleLog
    {
        internal const string Category = "CycloneGames.Cheat.Sample";
        internal const string BenchmarkCategory = "CycloneGames.Cheat.Sample.Benchmark";

        internal static readonly LogChannel Channel = LogChannel.Create(Category);
        internal static readonly LogChannel BenchmarkChannel = LogChannel.Create(BenchmarkCategory);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(
                Category,
                logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }

        internal static LogChannel CreateBenchmark(ILogWriter logWriter)
        {
            return LogChannel.Create(
                BenchmarkCategory,
                logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
