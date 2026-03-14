using UnityEngine;
using CycloneGames.Logger;
using System.Diagnostics;
using System.IO;
using System.Text;
using Unity.Profiling;

public class LoggerBenchmark : MonoBehaviour
{
    private const int TestIterations = 10000;
    private const int WarmupIterations = 512;
    private Stopwatch stopwatch = new Stopwatch();
    private string reportPath;

    private float unityLoggerTimespan = -1;
    private float customLoggerStringTimespan = -1;
    private float customLoggerBuilderTimespan = -1;
    private float customLoggerBuilderGenericTimespan = -1;
    private long unityLoggerGC = 0;
    private long customLoggerStringGC = 0;
    private long customLoggerBuilderGC = 0;
    private long customLoggerBuilderGenericGC = 0;

    private ProfilerRecorder gcAllocRecorder;

    void Start()
    {
        // Single-threaded mode: Pump() processes all messages synchronously within the measurement window.
        // This ensures GC from both enqueue and dispatch is captured accurately.
        CLogger.ConfigureSingleThreadedProcessing();

#if !UNITY_EDITOR
        CLogger.Instance.AddLoggerUnique(new ConsoleLogger());
#endif
        CLogger.Instance.AddLoggerUnique(new UnityLogger());
        CLogger.Instance.SetLogLevel(LogLevel.Trace);

        reportPath = Path.Combine(Application.dataPath, "Logs/LoggerBenchmarkReport.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(reportPath));

        StartCoroutine(RunBenchmarks());
    }

    System.Collections.IEnumerator RunBenchmarks()
    {
        gcAllocRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC.Alloc");

        // ---------- Phase 1: Warm all pools ----------
        for (int i = 0; i < WarmupIterations; i++)
        {
            CLogger.LogInfo(i, static (state, sb) => sb.Append("Warmup ").Append(state), "Benchmark");
        }
        CLogger.Instance.Pump(WarmupIterations * 2);
        yield return null;

        // Force GC to clean up warmup transients
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
        System.GC.Collect();
        yield return null;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        LogMessagePool.ResetStatistics();
        CycloneGames.Logger.Util.StringBuilderPool.ResetStatistics();
#endif

        // ---------- Phase 2: Unity Debug.Log baseline ----------
        TestUnityLogger();
        yield return new WaitForSeconds(0.1f);

        // ---------- Phase 3: CLogger String API ----------
        TestCustomLoggerString();
        CLogger.Instance.Pump(TestIterations * 2);
        yield return new WaitForSeconds(0.1f);

        // ---------- Phase 4: CLogger Builder API (closure lambda) ----------
        TestCustomLoggerBuilder();
        CLogger.Instance.Pump(TestIterations * 2);
        yield return new WaitForSeconds(0.1f);

        // ---------- Phase 5: CLogger Builder API (generic state, zero-closure) ----------
        TestCustomLoggerBuilderGeneric();
        CLogger.Instance.Pump(TestIterations * 2);
        yield return new WaitForSeconds(0.5f);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        ShowPoolStatistics();
#endif

        string finalReport = BuildFinalReport();
        File.AppendAllText(reportPath, finalReport, System.Text.Encoding.UTF8);
        UnityEngine.Debug.Log(finalReport);

        gcAllocRecorder.Dispose();
    }

    void OnDestroy()
    {
        CLogger.Instance.Dispose();
    }

    void Update()
    {
        CLogger.Instance.Pump(4096);
    }

    void TestUnityLogger()
    {
        UnityEngine.Debug.Log("Unity Logger Warmup");

        long gcBefore = gcAllocRecorder.CurrentValue;
        stopwatch.Restart();

        for (int i = 0; i < TestIterations; i++)
        {
            UnityEngine.Debug.Log($"Unity test message {i}");
        }

        stopwatch.Stop();
        long gcAfter = gcAllocRecorder.CurrentValue;
        unityLoggerGC = gcAfter - gcBefore;
        unityLoggerTimespan = stopwatch.ElapsedMilliseconds;

        string report = $"Unity Debug.Log - {TestIterations} iterations: {unityLoggerTimespan} ms, GC Alloc: {unityLoggerGC / 1024.0:F2} KB";
        File.AppendAllText(reportPath, report + "\n", System.Text.Encoding.UTF8);
        UnityEngine.Debug.Log(report);
    }

    void TestCustomLoggerString()
    {
        // String API: caller allocates the interpolated string, logger pools everything else.
        long gcBefore = gcAllocRecorder.CurrentValue;
        stopwatch.Restart();

        for (int i = 0; i < TestIterations; i++)
        {
            CLogger.LogInfo($"Custom test message {i}", "Benchmark");
        }
        // Pump inside measurement to capture dispatch + output GC
        CLogger.Instance.Pump(TestIterations * 2);

        stopwatch.Stop();
        long gcAfter = gcAllocRecorder.CurrentValue;
        customLoggerStringGC = gcAfter - gcBefore;
        customLoggerStringTimespan = stopwatch.ElapsedMilliseconds;

        string report = $"CLogger String API - {TestIterations} iterations: {customLoggerStringTimespan} ms, GC Alloc: {customLoggerStringGC / 1024.0:F2} KB";
        File.AppendAllText(reportPath, report + "\n");
        UnityEngine.Debug.Log(report);
    }

    void TestCustomLoggerBuilder()
    {
        // Builder API with closure: lambda captures loop variable i, causing closure allocation.
        long gcBefore = gcAllocRecorder.CurrentValue;
        stopwatch.Restart();

        for (int i = 0; i < TestIterations; i++)
        {
            CLogger.LogInfo(sb => sb.Append("Custom test message ").Append(i), "Benchmark");
        }
        CLogger.Instance.Pump(TestIterations * 2);

        stopwatch.Stop();
        long gcAfter = gcAllocRecorder.CurrentValue;
        customLoggerBuilderGC = gcAfter - gcBefore;
        customLoggerBuilderTimespan = stopwatch.ElapsedMilliseconds;

        string report = $"CLogger Builder (closure) - {TestIterations} iterations: {customLoggerBuilderTimespan} ms, GC Alloc: {customLoggerBuilderGC / 1024.0:F2} KB";
        File.AppendAllText(reportPath, report + "\n");
        UnityEngine.Debug.Log(report);
    }

    void TestCustomLoggerBuilderGeneric()
    {
        // Generic state API: static lambda, zero closure allocation. This is the recommended hot-path API.
        long gcBefore = gcAllocRecorder.CurrentValue;
        stopwatch.Restart();

        for (int i = 0; i < TestIterations; i++)
        {
            CLogger.LogInfo(i, static (state, sb) => sb.Append("Custom test message ").Append(state), "Benchmark");
        }
        CLogger.Instance.Pump(TestIterations * 2);

        stopwatch.Stop();
        long gcAfter = gcAllocRecorder.CurrentValue;
        customLoggerBuilderGenericGC = gcAfter - gcBefore;
        customLoggerBuilderGenericTimespan = stopwatch.ElapsedMilliseconds;

        string report = $"CLogger Builder (generic) - {TestIterations} iterations: {customLoggerBuilderGenericTimespan} ms, GC Alloc: {customLoggerBuilderGenericGC / 1024.0:F2} KB";
        File.AppendAllText(reportPath, report + "\n");
        UnityEngine.Debug.Log(report);
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    void ShowPoolStatistics()
    {
        var sbStats = CycloneGames.Logger.Util.StringBuilderPool.GetStatistics();
        var msgStats = LogMessagePool.GetStatistics();

        string statsReport = $@"
========================================
Object Pool Statistics
========================================
StringBuilder Pool:
  - Current Size: {sbStats.CurrentSize}
  - Peak Size: {sbStats.PeakSize}
  - Total Gets: {sbStats.TotalGets}
  - Total Returns: {sbStats.TotalReturns}
  - Pool Misses: {sbStats.TotalMisses} (new allocations)
  - Total Discards: {sbStats.TotalDiscards}
  - Trim Count: {sbStats.TrimCount}
  - Hit Rate: {sbStats.HitRate:P2}
  - Discard Rate: {sbStats.DiscardRate:P2}

LogMessage Pool:
  - Current Size: {msgStats.CurrentSize}
  - Peak Size: {msgStats.PeakSize}
  - Total Gets: {msgStats.TotalGets}
  - Total Returns: {msgStats.TotalReturns}
  - Pool Misses: {msgStats.TotalMisses} (new allocations)
  - Total Discards: {msgStats.TotalDiscards}
  - Trim Count: {msgStats.TrimCount}
  - Hit Rate: {msgStats.HitRate:P2}
  - Discard Rate: {msgStats.DiscardRate:P2}
========================================
";
        File.AppendAllText(reportPath, statsReport);
        UnityEngine.Debug.Log(statsReport);
    }
#endif

    string BuildFinalReport()
    {
        return $@"
{TestIterations} Iterations (Single-Threaded, Pools Pre-Warmed)
Final Comparison ({System.DateTime.Now}):
==========================================================================
| Logger Type                   | Time (ms) | GC (KB)    | Notes        |
|-------------------------------|-----------|------------|--------------|
| Unity Debug.Log               | {FormatFloat(unityLoggerTimespan)} | {FormatGC(unityLoggerGC)} | Baseline     |
| CLogger String API            | {FormatFloat(customLoggerStringTimespan)} | {FormatGC(customLoggerStringGC)} | Caller alloc |
| CLogger Builder (closure)     | {FormatFloat(customLoggerBuilderTimespan)} | {FormatGC(customLoggerBuilderGC)} | Has closure  |
| CLogger Builder (generic)     | {FormatFloat(customLoggerBuilderGenericTimespan)} | {FormatGC(customLoggerBuilderGenericGC)} | Zero closure |
==========================================================================

NOTES:
- Single-threaded mode ensures all GC (enqueue + dispatch) is measured deterministically.
- String API GC = caller's string interpolation + UnityLogger sb.ToString().
- Builder (closure): lambda captures loop variable => closure + delegate allocation.
- Builder (generic): static lambda with state parameter => zero closure, zero delegate allocation.
- Remaining GC in generic path = UnityLogger sb.ToString() (unavoidable for Unity Console output).
- In production without UnityLogger (file-only), generic API achieves true zero-GC.
";
    }

    static string FormatFloat(float number, int length = 9)
    {
        return number.ToString("F2").PadLeft(length);
    }

    static string FormatGC(long bytes)
    {
        return (bytes / 1024.0).ToString("F2").PadLeft(10);
    }
}