using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Text;
using CycloneGames.Logging;
using CycloneGames.Logging.Unity;
using UnityEngine;
using PipelineSink = CycloneGames.Logging.ILogSink;

public sealed class LoggingBenchmark : MonoBehaviour
{
    private const int Iterations = 10000;
    private const int ConsoleIterations = 1000;
    private const int WarmupIterations = 4096;
    private const int SteadyPumpBatchSize = 128;
    private const int QueueCapacityMultiplier = 4;

    private static object _allocationProbe;
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
    private static readonly Func<long> AllocatedBytesProvider = CreateAllocatedBytesProvider();
    private static readonly LogChannel Log = LoggingSamplesLog.BenchmarkChannel;

    private readonly Stopwatch _stopwatch = new Stopwatch();
    private readonly StringBuilder _reportBuilder = new StringBuilder(16384);

    private LogPipeline _pipeline;
    private LogChannel _benchmarkLog;
    private string _reportPath;
    private string _fileBenchmarkPath;

    private void Start()
    {
        string reportDirectory = Path.Combine(Application.temporaryCachePath, "CycloneGames.Logging");
        Directory.CreateDirectory(reportDirectory);
        _reportPath = Path.Combine(reportDirectory, "LoggingBenchmarkReport.txt");
        _fileBenchmarkPath = Path.Combine(reportDirectory, "LoggingBenchmarkFile.log");

        StartCoroutine(RunBenchmarks());
    }

    private IEnumerator RunBenchmarks()
    {
        AppendHeader();
        yield return WarmupPools();

        AddResult(MeasureLogPipeline(
            "LogPipeline Disabled Generic",
            "Disabled",
            Iterations,
            static () => new NullLogSink(),
            ConfigureDisabledLogSeverity,
            RunLogPipelineGenericSteady,
            "Info logs filtered by Error level; builder should not run."));
        yield return PrepareNextCase();

        AddResult(MeasureLogPipeline(
            "LogPipeline No Sink Generic",
            "NoSink",
            Iterations,
            null,
            ConfigureTraceLogSeverity,
            RunLogPipelineGenericSteady,
            "No registered sink; builder should not run."));
        yield return PrepareNextCase();

        AddResult(MeasureLogPipeline(
            "LogPipeline Core String Steady",
            "Core",
            Iterations,
            static () => new NullLogSink(),
            ConfigureTraceLogSeverity,
            RunLogPipelineStringSteady,
            "NullLogSink; Pump every 128 messages."));
        yield return PrepareNextCase();

        AddResult(MeasureLogPipeline(
            "LogPipeline Core Builder Closure Steady",
            "Core",
            Iterations,
            static () => new NullLogSink(),
            ConfigureTraceLogSeverity,
            RunLogPipelineBuilderClosureSteady,
            "NullLogSink; closure allocation path."));
        yield return PrepareNextCase();

        AddResult(MeasureLogPipeline(
            "LogPipeline Core Builder Generic Steady",
            "Core",
            Iterations,
            static () => new NullLogSink(),
            ConfigureTraceLogSeverity,
            RunLogPipelineGenericSteady,
            "NullLogSink; recommended hot-path API."));
        yield return PrepareNextCase();

        AddResult(MeasureLogPipeline(
            "LogPipeline Core Builder Generic Burst",
            "Burst",
            Iterations,
            static () => new NullLogSink(),
            ConfigureTraceLogSeverity,
            RunLogPipelineGenericBurst,
            "NullLogSink; enqueue all messages before Pump."));
        yield return PrepareNextCase();

#if !UNITY_WEBGL || UNITY_EDITOR
        AddResult(MeasureLogPipeline(
            "LogPipeline File Generic Steady",
            "File",
            Iterations,
            CreateFileLogSink,
            ConfigureTraceLogSeverity,
            RunLogPipelineGenericSteady,
            "FileLogSink sink; batched disk I/O."));
        yield return PrepareNextCase();
#endif

        AddResult(MeasureLogPipeline(
            "LogPipeline Unity Console Generic",
            "Console",
            ConsoleIterations,
            static () => new UnityConsoleLogSink(),
            ConfigureTraceLogSeverity,
            RunLogPipelineUnityConsoleGeneric,
            "UnityConsoleLogSink sink; hyperlink formatting and Console output."));

        AppendNotes();
        string report = _reportBuilder.ToString();
        File.WriteAllText(_reportPath, report, Utf8NoBom);
        Log.Info(report);
    }

    private void OnDestroy()
    {
        ShutdownPipeline();
    }

    private IEnumerator WarmupPools()
    {
        ConfigureSingleThreadedPipeline(static () => new NullLogSink(), ConfigureTraceLogSeverity);
        for (int i = 0; i < WarmupIterations; i++)
        {
            _benchmarkLog.Info(i, static (state, sb) => sb.Append("Warmup ").Append(state));
            if ((i + 1) % SteadyPumpBatchSize == 0)
            {
                _pipeline.Pump(SteadyPumpBatchSize);
            }
        }

        _pipeline.Pump(WarmupIterations);
        ShutdownPipeline();

        yield return PrepareNextCase();
    }

    private IEnumerator PrepareNextCase()
    {
        ForceFullGc();
        yield return null;
    }

    private BenchmarkResult MeasureLogPipeline(
        string name,
        string group,
        int iterations,
        Func<PipelineSink> sinkFactory,
        Action<LogPipeline> configurePipeline,
        Action action,
        string notes)
    {
        ConfigureSingleThreadedPipeline(sinkFactory, configurePipeline);

        ForceFullGc();
        CounterSnapshot before = CaptureCounterSnapshot();
        LogPipelineStatistics processingBefore = _pipeline.GetStatistics();

        _stopwatch.Restart();
        action();
        _stopwatch.Stop();

        LogPipelineStatistics processingAfter = _pipeline.GetStatistics();
        CounterSnapshot after = CaptureCounterSnapshot();
        ShutdownPipeline();

        return BenchmarkResult.Create(name, group, iterations, _stopwatch.Elapsed.TotalMilliseconds, before, after, processingBefore, processingAfter, notes);
    }

    private void ConfigureSingleThreadedPipeline(Func<PipelineSink> sinkFactory, Action<LogPipeline> configurePipeline)
    {
        ShutdownPipeline();
        if (_pipeline != null)
        {
            throw new InvalidOperationException("The previous benchmark pipeline did not finish shutting down.");
        }

        _pipeline = LogPipelineFactory.CreateSingleThreaded(new LogPipelineOptions
        {
            MaxQueuedMessages = Iterations * QueueCapacityMultiplier,
            OverflowPolicy = LogQueueOverflowPolicy.DropNewest,
            CriticalSeverity = LogSeverity.Error,
            ShutdownDrainTimeoutMs = 5000
        });
        _benchmarkLog = LoggingSamplesLog.CreateBenchmark(_pipeline);

        configurePipeline?.Invoke(_pipeline);
        if (sinkFactory != null)
        {
            _pipeline.TryAddSink(sinkFactory());
        }
    }

    private static void ConfigureTraceLogSeverity(LogPipeline pipeline)
    {
        pipeline.SetMinimumSeverity(LogSeverity.Trace);
    }

    private static void ConfigureDisabledLogSeverity(LogPipeline pipeline)
    {
        pipeline.SetMinimumSeverity(LogSeverity.Error);
    }

    private void RunLogPipelineStringSteady()
    {
        for (int i = 0; i < Iterations; i++)
        {
            _benchmarkLog.Info("Custom test message " + i);
            PumpSteady(i);
        }

        _pipeline.Pump(SteadyPumpBatchSize);
    }

    private void RunLogPipelineBuilderClosureSteady()
    {
        for (int i = 0; i < Iterations; i++)
        {
            _benchmarkLog.Info(sb => sb.Append("Custom test message ").Append(i));
            PumpSteady(i);
        }

        _pipeline.Pump(SteadyPumpBatchSize);
    }

    private void RunLogPipelineGenericSteady()
    {
        for (int i = 0; i < Iterations; i++)
        {
            _benchmarkLog.Info(i, static (state, sb) => sb.Append("Custom test message ").Append(state));
            PumpSteady(i);
        }

        _pipeline.Pump(SteadyPumpBatchSize);
    }

    private void RunLogPipelineGenericBurst()
    {
        for (int i = 0; i < Iterations; i++)
        {
            _benchmarkLog.Info(i, static (state, sb) => sb.Append("Custom test message ").Append(state));
        }

        _pipeline.Pump(Iterations * 2);
    }

    private void RunLogPipelineUnityConsoleGeneric()
    {
        for (int i = 0; i < ConsoleIterations; i++)
        {
            _benchmarkLog.Info(i, static (state, sb) => sb.Append("Custom test message ").Append(state));
            PumpSteady(i);
        }

        _pipeline.Pump(SteadyPumpBatchSize);
    }

    private void PumpSteady(int index)
    {
        if ((index + 1) % SteadyPumpBatchSize == 0)
        {
            _pipeline.Pump(SteadyPumpBatchSize);
        }
    }

    private void ShutdownPipeline()
    {
        LogPipeline pipeline = _pipeline;
        if (pipeline == null)
        {
            return;
        }

        LogPipelineShutdownResult result = pipeline.Shutdown(LogFlushMode.Buffered, 5000);
        if (result.IsComplete || result.Status == LogPipelineShutdownStatus.NotStarted)
        {
            _pipeline = null;
            _benchmarkLog = default;
        }
    }

#if !UNITY_WEBGL || UNITY_EDITOR
    private PipelineSink CreateFileLogSink()
    {
        if (File.Exists(_fileBenchmarkPath))
        {
            File.Delete(_fileBenchmarkPath);
        }

        return new FileLogSink(_fileBenchmarkPath, new FileLogSinkOptions
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
        LogMemoryPoolStatistics statistics = LogMemoryPools.GetStatistics();
        return new PoolSnapshot(
            statistics.StringBuilderPoolMisses,
            statistics.LogEventPoolMisses,
            statistics.StringBuilderPoolDiscards,
            statistics.LogEventPoolDiscards);
    }

    private void AppendHeader()
    {
        _reportBuilder.Length = 0;
        _reportBuilder.AppendLine();
        _reportBuilder.AppendLine("CycloneGames.Logging Benchmark");
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
        _reportBuilder.Append(result.LogEventPoolMisses.ToString().PadLeft(8));
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
        _reportBuilder.AppendLine("- Core cases use NullLogSink, so they measure LogPipeline filtering, message creation, queueing, Pump, and dispatch only.");
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
        public readonly long LogEventMisses;
        public readonly long StringBuilderDiscards;
        public readonly long LogEventDiscards;

        public PoolSnapshot(long stringBuilderMisses, long logEventMisses, long stringBuilderDiscards, long logEventDiscards)
        {
            StringBuilderMisses = stringBuilderMisses;
            LogEventMisses = logEventMisses;
            StringBuilderDiscards = stringBuilderDiscards;
            LogEventDiscards = logEventDiscards;
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
        public readonly long LogEventPoolMisses;
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
            long logEventPoolMisses,
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
            LogEventPoolMisses = logEventPoolMisses;
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
            LogPipelineStatistics processingBefore,
            LogPipelineStatistics processingAfter,
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
                after.Pool.LogEventMisses - before.Pool.LogEventMisses,
                processingAfter.DroppedMessageCount - processingBefore.DroppedMessageCount,
                notes);
        }
    }

    private sealed class NullLogSink : PipelineSink
    {
        public int Count { get; private set; }

        public void Emit(LogEvent logEvent)
        {
            Count++;
        }

        public void Dispose()
        {
        }
    }
}
