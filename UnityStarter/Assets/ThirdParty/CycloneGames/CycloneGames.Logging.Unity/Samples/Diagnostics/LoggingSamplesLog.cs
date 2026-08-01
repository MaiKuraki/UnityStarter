using System;
using CycloneGames.Logging;

internal static class LoggingSamplesLog
{
    internal const string Category = "CycloneGames.Logging.Sample";
    internal const string LoadCategory = "CycloneGames.Logging.LoadSample";
    internal const string PoolMonitorCategory = "CycloneGames.Logging.PoolMonitor";
    internal const string BenchmarkCategory = "CycloneGames.Logging.Benchmark";

    internal static readonly LogChannel Channel = LogChannel.Create(Category);
    internal static readonly LogChannel LoadChannel = LogChannel.Create(LoadCategory);
    internal static readonly LogChannel PoolMonitorChannel = LogChannel.Create(PoolMonitorCategory);
    internal static readonly LogChannel BenchmarkChannel = LogChannel.Create(BenchmarkCategory);

    internal static LogChannel Create(ILogWriter logWriter)
    {
        return LogChannel.Create(Category, logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
    }

    internal static LogChannel CreateBenchmark(ILogWriter logWriter)
    {
        return LogChannel.Create(BenchmarkCategory, logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
    }
}
