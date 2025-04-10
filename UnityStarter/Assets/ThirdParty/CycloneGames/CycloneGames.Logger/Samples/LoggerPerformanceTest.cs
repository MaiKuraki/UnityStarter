using UnityEngine;
using CycloneGames.Logger;

public class LoggerPerformanceTest : MonoBehaviour
{
    private int logCount = 0;
    private const int MaxLogCount = 10000; // Maximum number of logs to test performance
    private float startTime;

    void Start()
    {
        // Initialize logger system with different loggers
        CLogger.Instance.AddLogger(new ConsoleLogger());
        CLogger.Instance.AddLogger(new FileLogger(Application.dataPath + "/Logs/PerformanceTest.log"));
        CLogger.Instance.AddLogger(new UnityLogger());

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
        if (logCount < MaxLogCount)
        {
            // Log messages at different severity levels
            CLogger.LogTrace($"Trace log message {logCount}", "PerformanceTest");
            CLogger.LogDebug($"Debug log message {logCount}", "PerformanceTest");
            CLogger.LogInfo($"Info log message {logCount}", "PerformanceTest");
            CLogger.LogWarning($"Warning log message {logCount}", "PerformanceTest");
            CLogger.LogError($"Error log message {logCount}", "PerformanceTest");
            CLogger.LogFatal($"Fatal log message {logCount}", "PerformanceTest");

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