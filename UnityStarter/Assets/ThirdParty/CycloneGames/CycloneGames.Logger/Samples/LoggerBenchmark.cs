using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Text;
using CycloneGames.Logger;
using CycloneGames.Logging;
using UnityEngine;
using CgILogger = CycloneGames.Logger.ILogger;

public sealed class LoggerBenchmark : MonoBehaviour
{
    private const int Iterations = 10000;
    private const int ConsoleIterations = 1000;
    private const int WarmupIterations = 4096;
    private const int SteadyPumpBatchSize = 128;
    private const int QueueCapacityMultiplier = 4;

    private static object _allocationProbe;
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
    private static readonly Func<long> AllocatedBytesProvider = CreateAllocatedBytesProvider();
    private static readonly LogChannel Log = LoggerSamplesLog.BenchmarkChannel;

    private readonly Stopwatch _stopwatch = new Stopwatch();
    private readonly StringBuilder _reportBuilder = new StringBuilder(16384);

    private CLogger _logger;
    private LogChannel _benchmarkLog;
    private string _reportPath;
    private string _fileBenchmarkPath;

    private void Start()
    {
        string reportDirectory = Path.Combine(Application.temporaryCachePath, "CycloneGames.Logger");
        Directory.CreateDirectory(reportDirectory);
        _reportPath = Path.Combine(reportDirectory, "LoggerBenchmarkReport.txt");
        _fileBenchmarkPath = Path.Combine(reportDirectory, "LoggerBenchmarkFile.log");

        StartCoroutine(RunBenchmarks());
    }

    private IEnumerator RunBenchmarks()
    {
        AppendHeader();
        yield return WarmupPools();

        AddResult(MeasureCLogger(
            "CLogger Disabled Generic",
            "Disabled",
            Iterations,
            static () => new NullLogger(),
            ConfigureDisabledLogLevel,
            RunCLoggerGenericSteady,
            "Info logs filtered by Error level; builder should not run."));
        yield return PrepareNextCase();

        AddResult(MeasureCLogger(
            "CLogger No Sink Generic",
            "NoSink",
            Iterations,
            null,
            ConfigureTraceLogLevel,
            RunCLoggerGenericSteady,
            "No registered sink; builder should not run."));
        yield return PrepareNextCase();

        AddResult(MeasureCLogger(
            "CLogger Core String Steady",
            "Core",
            Iterations,
            static () => new NullLogger(),
            ConfigureTraceLogLevel,
            RunCLoggerStringSteady,
            "NullLogger sink; Pump every 128 messages."));
        yield return PrepareNextCase();

        AddResult(MeasureCLogger(
            "CLogger Core Builder Closure Steady",
            "Core",
            Iterations,
            static () => new NullLogger(),
            ConfigureTraceLogLevel,
            RunCLoggerBuilderClosureSteady,
            "NullLogger sink; closure allocation path."));
        yield return PrepareNextCase();

        AddResult(MeasureCLogger(
            "CLogger Core Builder Generic Steady",
            "Core",
            Iterations,
            static () => new NullLogger(),
            ConfigureTraceLogLevel,
            RunCLoggerGenericSteady,
            "NullLogger sink; recommended hot-path API."));
        yield return PrepareNextCase();

        AddResult(MeasureCLogger(
            "CLogger Core Builder Generic Burst",
            "Burst",
            Iterations,
            static () => new NullLogger(),
            ConfigureTraceLogLevel,
            RunCLoggerGenericBurst,
            "NullLogger sink; enqueue all messages before Pump."));
        yield return PrepareNextCase();

#if !UNITY_WEBGL || UNITY_EDITOR
        AddResult(MeasureCLogger(
            "CLogger File Generic Steady",
            "File",
            Iterations,
            CreateFileLogger,
            ConfigureTraceLogLevel,
            RunCLoggerGenericSteady,
            "FileLogger sink; batched disk I/O."));
        yield return PrepareNextCase();
#endif

        AddResult(MeasureCLogger(
            "CLogger Unity Console Generic",
            "Console",
            ConsoleIterations,
            static () => new UnityLogger(),
            ConfigureTraceLogLevel,
            RunCLoggerUnityConsoleGeneric,
            "UnityLogger sink; hyperlink formatting and Console output."));

        AppendNotes();
        string report = _reportBuilder.ToString();
        File.WriteAllText(_reportPath, report, Utf8NoBom);
        Log.Info(report);
    }

    private void OnDestroy()
    {
        ShutdownLogger();
    }

    private IEnumerator WarmupPools()
    {
        ConfigureSingleThreadedLogger(static () => new NullLogger(), ConfigureTraceLogLevel);
        for (int i = 0; i < WarmupIterations; i++)
        {
            _benchmarkLog.Info(i, static (state, sb) => sb.Append("Warmup ").Append(state));
            if ((i + 1) % SteadyPumpBatchSize == 0)
            {
                _logger.Pump(SteadyPumpBatchSize);
            }
        }

        _logger.Pump(WarmupIterations);
        ShutdownLogger();

        yield return PrepareNextCase();
    }

    private IEnumerator PrepareNextCase()
    {
        ForceFullGc();
        yield return null;
    }

    private BenchmarkResult MeasureCLogger(
        string name,
        string group,
        int iterations,
        Func<CgILogger> loggerFactory,
        Action<CLogger> configureLogger,
        Action action,
        string notes)
    {
        ConfigureSingleThreadedLogger(loggerFactory, configureLogger);

        ForceFullGc();
        CounterSnapshot before = CaptureCounterSnapshot();
        LogProcessingStatistics processingBefore = _logger.GetProcessingStatistics();

        _stopwatch.Restart();
        action();
        _stopwatch.Stop();

        LogProcessingStatistics processingAfter = _logger.GetProcessingStatistics();
        CounterSnapshot after = CaptureCounterSnapshot();
        ShutdownLogger();

        return BenchmarkResult.Create(name, group, iterations, _stopwatch.Elapsed.TotalMilliseconds, before, after, processingBefore, processingAfter, notes);
    }

    private void ConfigureSingleThreadedLogger(Func<CgILogger> loggerFactory, Action<CLogger> configureLogger)
    {
        ShutdownLogger();
        if (_logger != null)
        {
            throw new InvalidOperationException("The previous benchmark logger did not finish shutting down.");
        }

        _logger = CLoggerFactory.CreateSingleThreaded(new LoggerProcessingOptions
        {
            MaxQueuedMessages = Iterations * QueueCapacityMultiplier,
            UnityConsoleMaxQueuedMessages = ConsoleIterations * QueueCapacityMultiplier,
            OverflowPolicy = LogQueueOverflowPolicy.DropNewest,
            CriticalLevel = LogLevel.Error,
            ShutdownDrainTimeoutMs = 5000
        });
        _benchmarkLog = LoggerSamplesLog.CreateBenchmark(_logger);

        configureLogger?.Invoke(_logger);
        if (loggerFactory != null)
        {
            _logger.AddLoggerUnique(loggerFactory());
        }
    }

    private static void ConfigureTraceLogLevel(CLogger logger)
    {
        logger.SetLogLevel(LogLevel.Trace);
    }

    private static void ConfigureDisabledLogLevel(CLogger logger)
    {
        logger.SetLogLevel(LogLevel.Error);
    }

    private void RunCLoggerStringSteady()
    {
        for (int i = 0; i < Iterations; i++)
        {
            _benchmarkLog.Info("Custom test message " + i);
            PumpSteady(i);
        }

        _logger.Pump(SteadyPumpBatchSize);
    }

    private void RunCLoggerBuilderClosureSteady()
    {
        for (int i = 0; i < Iterations; i++)
        {
            _benchmarkLog.Info(sb => sb.Append("Custom test message ").Append(i));
            PumpSteady(i);
        }

        _logger.Pump(SteadyPumpBatchSize);
    }

    private void RunCLoggerGenericSteady()
    {
        for (int i = 0; i < Iterations; i++)
        {
            _benchmarkLog.Info(i, static (state, sb) => sb.Append("Custom test message ").Append(state));
            PumpSteady(i);
        }

        _logger.Pump(SteadyPumpBatchSize);
    }

    private void RunCLoggerGenericBurst()
    {
        for (int i = 0; i < Iterations; i++)
        {
            _benchmarkLog.Info(i, static (state, sb) => sb.Append("Custom test message ").Append(state));
        }

        _logger.Pump(Iterations * 2);
    }

    private void RunCLoggerUnityConsoleGeneric()
    {
        for (int i = 0; i < ConsoleIterations; i++)
        {
            _benchmarkLog.Info(i, static (state, sb) => sb.Append("Custom test message ").Append(state));
            PumpSteady(i);
        }

        _logger.Pump(SteadyPumpBatchSize);
    }

    private void PumpSteady(int index)
    {
        if ((index + 1) % SteadyPumpBatchSize == 0)
        {
            _logger.Pump(SteadyPumpBatchSize);
        }
    }

    private void ShutdownLogger()
    {
        CLogger logger = _logger;
        if (logger == null)
        {
            return;
        }

        LoggerShutdownResult result = logger.ShutdownInstance(LogFlushMode.Buffered, 5000);
        if (result.IsComplete || result.Status == LoggerShutdownStatus.NotStarted)
        {
            _logger = null;
            _benchmarkLog = default;
        }
    }

#if !UNITY_WEBGL || UNITY_EDITOR
    private CgILogger CreateFileLogger()
    {
        if (File.Exists(_fileBenchmarkPath))
        {
            File.Delete(_fileBenchmarkPath);
        }

        return new FileLogger(_fileBenchmarkPath, new FileLoggerOptions
        {
            MaintenanceMode = FileMaintenanceMode.None,
            FlushBatchSize = 1024,
            FlushIntervalMs = 60000
        });
    }
#endif

    private CounterSnapshot CaptureCounterSnapshot()
    {
        return new CounterSnapshot(
            GetAllocatedBytes(),
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2),
            CapturePoolSnapshot());
    }

    private static PoolSnapshot CapturePoolSnapshot()
    {
        LoggerMemoryStatistics statistics = CLogger.GetMemoryStatistics();
        return new PoolSnapshot(
            statistics.StringBuilderPoolMisses,
            statistics.LogMessagePoolMisses,
            statistics.StringBuilderPoolDiscards,
            statistics.LogMessagePoolDiscards);
    }

    private void AppendHeader()
    {
        _reportBuilder.Length = 0;
        _reportBuilder.AppendLine();
        _reportBuilder.AppendLine("CycloneGames.Logger Benchmark");
        _reportBuilder.AppendLine("================================================================================================================");
        _reportBuilder.Append("Started: ");
        _reportBuilder.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        _reportBuilder.Append("Iterations: ");
        _reportBuilder.Append(Iterations);
        _reportBuilder.Append(", Console Iterations: ");
        _reportBuilder.Append(ConsoleIterations);
        _reportBuilder.Append(", Steady Pump Batch: ");
        _reportBuilder.AppendLine(SteadyPumpBatchSize.ToString());
        _reportBuilder.Append("Report Path: ");
        _reportBuilder.AppendLine(_reportPath);
        _reportBuilder.Append("Allocation Counter: ");
        _reportBuilder.AppendLine(AllocatedBytesProvider == null ? "Unavailable" : "GC.GetAllocatedBytesForCurrentThread");
        _reportBuilder.AppendLine();
        _reportBuilder.AppendLine("| Group    | Scenario                         | Iterations | Time (ms) | us/log | logs/sec | Alloc (KB) | Gen0 | SB Miss | Msg Miss | Dropped | Notes");
        _reportBuilder.AppendLine("|----------|----------------------------------|------------|-----------|--------|----------|------------|------|---------|----------|---------|-----------------------------------------");
    }

    private void AddResult(BenchmarkResult result)
    {
        _reportBuilder.Append("| ");
        _reportBuilder.Append(result.Group.PadRight(8));
        _reportBuilder.Append(" | ");
        _reportBuilder.Append(result.Name.PadRight(32));
        _reportBuilder.Append(" | ");
        _reportBuilder.Append(result.Iterations.ToString().PadLeft(10));
        _reportBuilder.Append(" | ");
        _reportBuilder.Append(result.ElapsedMilliseconds.ToString("F2").PadLeft(9));
        _reportBuilder.Append(" | ");
        _reportBuilder.Append(result.MicrosecondsPerLog.ToString("F2").PadLeft(6));
        _reportBuilder.Append(" | ");
        _reportBuilder.Append(result.LogsPerSecond.ToString("F0").PadLeft(8));
        _reportBuilder.Append(" | ");
        _reportBuilder.Append(FormatGc(result.AllocatedBytes).PadLeft(10));
        _reportBuilder.Append(" | ");
        _reportBuilder.Append(result.Gen0Collections.ToString().PadLeft(4));
        _reportBuilder.Append(" | ");
        _reportBuilder.Append(result.StringBuilderPoolMisses.ToString().PadLeft(7));
        _reportBuilder.Append(" | ");
        _reportBuilder.Append(result.LogMessagePoolMisses.ToString().PadLeft(8));
        _reportBuilder.Append(" | ");
        _reportBuilder.Append(result.DroppedMessages.ToString().PadLeft(7));
        _reportBuilder.Append(" | ");
        _reportBuilder.AppendLine(result.Notes);
    }

    private void AppendNotes()
    {
        _reportBuilder.AppendLine("================================================================================================================");
        _reportBuilder.AppendLine();
        _reportBuilder.AppendLine("Interpretation:");
        _reportBuilder.AppendLine("- Steady cases pump every 128 messages; they model normal frame-by-frame logging.");
        _reportBuilder.AppendLine("- Burst cases enqueue all messages before Pump; they intentionally expose pool growth and memory pressure.");
        _reportBuilder.AppendLine("- NoSink measures an explicit initialized backend without registered sinks; the bound channel still preserves the unified producer API.");
        _reportBuilder.AppendLine("- Core cases use NullLogger, so they measure CLogger filtering, message creation, queueing, Pump, and dispatch only.");
        _reportBuilder.AppendLine("- File and Unity Console cases use the generic API but include sink-specific formatting and output costs.");
        _reportBuilder.AppendLine("- Unity Console is isolated because platform Console delivery and hyperlink formatting dominate both time and allocations.");
        _reportBuilder.AppendLine("- Alloc may be N/A or zero on runtimes where GC.GetAllocatedBytesForCurrentThread is unsupported; pool miss columns still reveal logger-owned allocations.");
        _reportBuilder.AppendLine("- us/log and logs/sec are the best columns for comparing cases with different iteration counts.");
        _reportBuilder.AppendLine("- Dropped should stay 0. Any positive value means the queue capacity or overflow policy affected the result.");
    }

    private static void ForceFullGc()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static long GetAllocatedBytes()
    {
        return AllocatedBytesProvider == null ? -1L : AllocatedBytesProvider();
    }

    private static Func<long> CreateAllocatedBytesProvider()
    {
        try
        {
            var method = typeof(GC).GetMethod("GetAllocatedBytesForCurrentThread", Type.EmptyTypes);
            if (method == null) return null;

            var provider = (Func<long>)Delegate.CreateDelegate(typeof(Func<long>), method);
            long before = provider();
            _allocationProbe = new byte[4096];
            long after = provider();
            return after > before ? provider : null;
        }
        catch
        {
            return null;
        }
    }

    private static string FormatGc(long bytes)
    {
        return bytes < 0 ? "N/A" : (bytes / 1024.0).ToString("F2");
    }

    private readonly struct CounterSnapshot
    {
        public readonly long AllocatedBytes;
        public readonly int Gen0Collections;
        public readonly int Gen1Collections;
        public readonly int Gen2Collections;
        public readonly PoolSnapshot Pool;

        public CounterSnapshot(long allocatedBytes, int gen0Collections, int gen1Collections, int gen2Collections, PoolSnapshot pool)
        {
            AllocatedBytes = allocatedBytes;
            Gen0Collections = gen0Collections;
            Gen1Collections = gen1Collections;
            Gen2Collections = gen2Collections;
            Pool = pool;
        }
    }

    private readonly struct PoolSnapshot
    {
        public readonly long StringBuilderMisses;
        public readonly long LogMessageMisses;
        public readonly long StringBuilderDiscards;
        public readonly long LogMessageDiscards;

        public PoolSnapshot(long stringBuilderMisses, long logMessageMisses, long stringBuilderDiscards, long logMessageDiscards)
        {
            StringBuilderMisses = stringBuilderMisses;
            LogMessageMisses = logMessageMisses;
            StringBuilderDiscards = stringBuilderDiscards;
            LogMessageDiscards = logMessageDiscards;
        }
    }

    private readonly struct BenchmarkResult
    {
        public readonly string Name;
        public readonly string Group;
        public readonly int Iterations;
        public readonly double ElapsedMilliseconds;
        public readonly double MicrosecondsPerLog;
        public readonly double LogsPerSecond;
        public readonly long AllocatedBytes;
        public readonly int Gen0Collections;
        public readonly long StringBuilderPoolMisses;
        public readonly long LogMessagePoolMisses;
        public readonly long DroppedMessages;
        public readonly string Notes;

        private BenchmarkResult(
            string name,
            string group,
            int iterations,
            double elapsedMilliseconds,
            double microsecondsPerLog,
            double logsPerSecond,
            long allocatedBytes,
            int gen0Collections,
            long stringBuilderPoolMisses,
            long logMessagePoolMisses,
            long droppedMessages,
            string notes)
        {
            Name = name;
            Group = group;
            Iterations = iterations;
            ElapsedMilliseconds = elapsedMilliseconds;
            MicrosecondsPerLog = microsecondsPerLog;
            LogsPerSecond = logsPerSecond;
            AllocatedBytes = allocatedBytes;
            Gen0Collections = gen0Collections;
            StringBuilderPoolMisses = stringBuilderPoolMisses;
            LogMessagePoolMisses = logMessagePoolMisses;
            DroppedMessages = droppedMessages;
            Notes = notes;
        }

        public static BenchmarkResult Create(
            string name,
            string group,
            int iterations,
            double elapsedMilliseconds,
            CounterSnapshot before,
            CounterSnapshot after,
            LogProcessingStatistics processingBefore,
            LogProcessingStatistics processingAfter,
            string notes)
        {
            long allocatedBytes = before.AllocatedBytes >= 0 && after.AllocatedBytes >= before.AllocatedBytes
                ? after.AllocatedBytes - before.AllocatedBytes
                : -1L;
            double microsecondsPerLog = iterations > 0 ? elapsedMilliseconds * 1000.0 / iterations : 0.0;
            double logsPerSecond = elapsedMilliseconds > 0.0 ? iterations * 1000.0 / elapsedMilliseconds : 0.0;

            return new BenchmarkResult(
                name,
                group,
                iterations,
                elapsedMilliseconds,
                microsecondsPerLog,
                logsPerSecond,
                allocatedBytes,
                after.Gen0Collections - before.Gen0Collections,
                after.Pool.StringBuilderMisses - before.Pool.StringBuilderMisses,
                after.Pool.LogMessageMisses - before.Pool.LogMessageMisses,
                processingAfter.DroppedMessageCount - processingBefore.DroppedMessageCount,
                notes);
        }
    }

    private sealed class NullLogger : CgILogger
    {
        public int Count { get; private set; }

        public void Log(LogMessage logMessage)
        {
            Count++;
        }

        public void Dispose()
        {
        }
    }
}
