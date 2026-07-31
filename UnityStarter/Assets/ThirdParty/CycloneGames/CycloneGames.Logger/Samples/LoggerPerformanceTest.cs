using System.IO;
using CycloneGames.Logger;
using CycloneGames.Logging;
using UnityEngine;

/// <summary>
/// Generates a finite mixed-severity load for manual observation. Use the package test
/// benchmarks, not this MonoBehaviour, for reproducible performance evidence.
/// </summary>
public sealed class LoggerPerformanceTest : MonoBehaviour
{
    private const int MaxLogCount = 10000;
    private static readonly LogChannel Log = LoggerSamplesLog.LoadChannel;

    private CLogger _backend;
    private FileLogger _fileLogger;
    private int _logCount;
    private float _startTime;

    private void Start()
    {
        _backend = LogRuntime.Writer as CLogger;
        if (_backend == null)
        {
            Log.Warning("The load sample requires CycloneGames.Logger to be installed as the process writer.");
            enabled = false;
            return;
        }

#if !UNITY_WEBGL || UNITY_EDITOR
        string path = Path.Combine(Application.temporaryCachePath, "CycloneGames.Logger", "LoadExample.log");
        _fileLogger = new FileLogger(path);
        if (!_backend.AddLoggerUnique(_fileLogger))
        {
            _fileLogger = null;
        }
#endif
        _backend.SetLogLevel(LogLevel.Trace);
        _startTime = Time.time;
    }

    private void Update()
    {
        if (_logCount >= MaxLogCount)
        {
            Log.Info($"Submitted {MaxLogCount} sample messages in {Time.time - _startTime:F2} seconds.");
            enabled = false;
            return;
        }

        int value = _logCount;
        Log.Trace(value, AppendTrace);
        Log.Debug(value, AppendDebug);
        Log.Info(value, AppendInfo);
        Log.Warning(value, AppendWarning);
        Log.Error(value, AppendError);
        Log.Fatal(value, AppendFatal);
        _logCount += 6;
    }

    private void OnDestroy()
    {
        if (_fileLogger == null)
        {
            return;
        }

        if (_backend != null && _backend.RemoveLogger(_fileLogger, 2000))
        {
            _fileLogger.Dispose();
        }

        _fileLogger = null;
        _backend = null;
    }

    private static void AppendTrace(int value, System.Text.StringBuilder builder) => builder.Append("Trace message ").Append(value);
    private static void AppendDebug(int value, System.Text.StringBuilder builder) => builder.Append("Debug message ").Append(value);
    private static void AppendInfo(int value, System.Text.StringBuilder builder) => builder.Append("Info message ").Append(value);
    private static void AppendWarning(int value, System.Text.StringBuilder builder) => builder.Append("Warning message ").Append(value);
    private static void AppendError(int value, System.Text.StringBuilder builder) => builder.Append("Error message ").Append(value);
    private static void AppendFatal(int value, System.Text.StringBuilder builder) => builder.Append("Fatal message ").Append(value);
}
