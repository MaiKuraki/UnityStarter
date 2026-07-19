using System.IO;
using CycloneGames.Logger;
using UnityEngine;

/// <summary>
/// Generates a finite mixed-severity load for manual observation. Use the package test
/// benchmarks, not this MonoBehaviour, for reproducible performance evidence.
/// </summary>
public sealed class LoggerPerformanceTest : MonoBehaviour
{
    private const int MaxLogCount = 10000;

    private FileLogger _fileLogger;
    private int _logCount;
    private float _startTime;

    private void Start()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        string path = Path.Combine(Application.temporaryCachePath, "CycloneGames.Logger", "LoadExample.log");
        _fileLogger = new FileLogger(path);
        CLogger.Instance.AddLoggerUnique(_fileLogger);
#endif
        CLogger.Instance.SetLogLevel(LogLevel.Trace);
        _startTime = Time.time;
    }

    private void Update()
    {
        if (_logCount >= MaxLogCount)
        {
            Debug.Log($"Submitted {MaxLogCount} sample messages in {Time.time - _startTime:F2} seconds.");
            enabled = false;
            return;
        }

        int value = _logCount;
        CLogger.LogTrace(value, AppendTrace, "LoadSample");
        CLogger.LogDebug(value, AppendDebug, "LoadSample");
        CLogger.LogInfo(value, AppendInfo, "LoadSample");
        CLogger.LogWarning(value, AppendWarning, "LoadSample");
        CLogger.LogError(value, AppendError, "LoadSample");
        CLogger.LogFatal(value, AppendFatal, "LoadSample");
        _logCount += 6;
    }

    private void OnDestroy()
    {
        if (_fileLogger == null)
        {
            return;
        }

        if (CLogger.Instance.RemoveLogger(_fileLogger, 2000))
        {
            _fileLogger.Dispose();
        }

        _fileLogger = null;
    }

    private static void AppendTrace(int value, System.Text.StringBuilder builder) => builder.Append("Trace message ").Append(value);
    private static void AppendDebug(int value, System.Text.StringBuilder builder) => builder.Append("Debug message ").Append(value);
    private static void AppendInfo(int value, System.Text.StringBuilder builder) => builder.Append("Info message ").Append(value);
    private static void AppendWarning(int value, System.Text.StringBuilder builder) => builder.Append("Warning message ").Append(value);
    private static void AppendError(int value, System.Text.StringBuilder builder) => builder.Append("Error message ").Append(value);
    private static void AppendFatal(int value, System.Text.StringBuilder builder) => builder.Append("Fatal message ").Append(value);
}
