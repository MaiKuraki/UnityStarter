using UnityEngine;
using CycloneGames.Logger;

public class LoggerPerformanceTest : MonoBehaviour
{
    private int logCount = 0;
    private const int MaxLogCount = 10000; // Maximum number of logs to test performance
    private float startTime;

    void Start()
    {
        // Remove if centralized bootstrap is used.
#if UNITY_WEBGL && !UNITY_EDITOR
        CLogger.ConfigureSingleThreadedProcessing();
#else
        CLogger.ConfigureThreadedProcessing();
#endif
        // Per-script registration (remove if centralized bootstrap registers loggers).
        // Avoid ConsoleLogger in the Unity Editor to prevent duplicate console entries.
#if !UNITY_EDITOR
        CLogger.Instance.AddLoggerUnique(new ConsoleLogger());
#endif
        CLogger.Instance.AddLoggerUnique(new FileLogger(Application.dataPath + "/Logs/PerformanceTest.log"));
        CLogger.Instance.AddLoggerUnique(new UnityLogger());

        // Configure logging level and filter
        CLogger.Instance.SetLogLevel(LogLevel.Trace);
        CLogger.Instance.SetLogFilter(LogFilter.LogAll);

        // Record test start time
        startTime = Time.time;
    }

    void OnDestroy()
    {
        CLogger.Instance.Dispose();
    }

    void Update()
    {
        // Pump drains the queue in single-threaded mode; no-op when threaded.
        CLogger.Instance.Pump(8192);
        if (logCount < MaxLogCount)
        {
            // Log messages at different severity levels
            CLogger.LogTrace(sb => { sb.Append("Trace log message "); sb.Append(logCount); }, "PerformanceTest");
            CLogger.LogDebug(sb => { sb.Append("Debug log message "); sb.Append(logCount); }, "PerformanceTest");
            CLogger.LogInfo(sb => { sb.Append("Info log message "); sb.Append(logCount); }, "PerformanceTest");
            CLogger.LogWarning(sb => { sb.Append("Warning log message "); sb.Append(logCount); }, "PerformanceTest");
            CLogger.LogError(sb => { sb.Append("Error log message "); sb.Append(logCount); }, "PerformanceTest");
            CLogger.LogFatal(sb => { sb.Append("Fatal log message "); sb.Append(logCount); }, "PerformanceTest");

            logCount += 6; // Increment counter (6 logs per Update)
        }
        else
        {
            // Calculate and display test duration 
            float elapsedTime = Time.time - startTime;
            Debug.Log($"Logged {MaxLogCount} messages in {elapsedTime} seconds.");

            // Clean up logger resources
            CLogger.Instance.Dispose();

            // Disable this script to stop testing
            this.enabled = false;
        }
    }
}