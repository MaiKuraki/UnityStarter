using UnityEngine;
using CycloneGames.Logger;

public class LoggerPerformanceTest : MonoBehaviour
{
    private int logCount = 0;
    private const int MaxLogCount = 10000;
    private float startTime;

    void Start()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        CLogger.ConfigureSingleThreadedProcessing();
#else
        CLogger.ConfigureThreadedProcessing();
#endif

#if !UNITY_EDITOR
        CLogger.Instance.AddLoggerUnique(new ConsoleLogger());
#endif
        CLogger.Instance.AddLoggerUnique(new FileLogger(Application.dataPath + "/Logs/PerformanceTest.log"));
        CLogger.Instance.AddLoggerUnique(new UnityLogger());

        CLogger.Instance.SetLogLevel(LogLevel.Trace);
        CLogger.Instance.SetLogFilter(LogFilter.LogAll);

        startTime = Time.time;
    }

    void OnDestroy()
    {
        CLogger.Instance.Dispose();
    }

    void Update()
    {
        CLogger.Instance.Pump(8192);
        
        if (logCount < MaxLogCount)
        {
            CLogger.LogTrace(sb => { sb.Append("Trace log message "); sb.Append(logCount); }, "PerformanceTest");
            CLogger.LogDebug(sb => { sb.Append("Debug log message "); sb.Append(logCount); }, "PerformanceTest");
            CLogger.LogInfo(sb => { sb.Append("Info log message "); sb.Append(logCount); }, "PerformanceTest");
            CLogger.LogWarning(sb => { sb.Append("Warning log message "); sb.Append(logCount); }, "PerformanceTest");
            CLogger.LogError(sb => { sb.Append("Error log message "); sb.Append(logCount); }, "PerformanceTest");
            CLogger.LogFatal(sb => { sb.Append("Fatal log message "); sb.Append(logCount); }, "PerformanceTest");

            logCount += 6;
        }
        else
        {
            float elapsedTime = Time.time - startTime;
            Debug.Log($"Logged {MaxLogCount} messages in {elapsedTime:F2} seconds.");

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ShowPoolStatistics();
#endif

            CLogger.Instance.Dispose();
            this.enabled = false;
        }
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    void ShowPoolStatistics()
    {
        var sbStats = CycloneGames.Logger.Util.StringBuilderPool.GetStatistics();
        var msgStats = LogMessagePool.GetStatistics();

        Debug.Log($@"
=== Performance Test Results ===
StringBuilder Pool: Peak={sbStats.PeakSize}, Discards={sbStats.TotalDiscards}
LogMessage Pool: Peak={msgStats.PeakSize}, Discards={msgStats.TotalDiscards}
Note: Discards should be 0 for optimal performance!
");
    }
#endif
}