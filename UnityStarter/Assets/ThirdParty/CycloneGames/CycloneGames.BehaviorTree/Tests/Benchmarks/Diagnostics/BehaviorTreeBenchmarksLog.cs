using System;
using CycloneGames.Logging;

namespace CycloneGames.BehaviorTree.Runtime.PerformanceTest
{
    internal static class BehaviorTreeBenchmarksLog
    {
        internal const string Category = "CycloneGames.BehaviorTree.Benchmarks";

        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(
                Category,
                logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
