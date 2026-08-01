using System.IO;
using CycloneGames.Logging;
using UnityEngine;

/// <summary>
/// Generates a finite mixed-severity load for manual observation. Use the package test
/// benchmarks, not this MonoBehaviour, for reproducible performance evidence.
/// </summary>
public sealed class LoggingPerformanceTest : MonoBehaviour
{
    private const int MaxLogCount = 10000;
    private static readonly LogChannel Log = LoggingSamplesLog.LoadChannel;

    private LogPipeline _pipeline;
    private FileLogSink _fileSink;
    private LogSeverity _previousMinimumSeverity;
    private int _logCount;
    private float _startTime;
    private bool _minimumSeverityChanged;

    private void Start()
    {
        _pipeline = LogRuntime.Writer as LogPipeline;
        if (_pipeline == null)
        {
            Log.Warning("The load sample requires a LogPipeline to be installed as the process writer.");
            enabled = false;
            return;
        }

#if !UNITY_WEBGL || UNITY_EDITOR
        string path = Path.Combine(Application.temporaryCachePath, "CycloneGames.Logging", "LoadExample.log");
        _fileSink = new FileLogSink(path);
        if (!_pipeline.TryAddSink(_fileSink))
        {
            _fileSink = null;
        }
#endif
        _previousMinimumSeverity = _pipeline.MinimumSeverity;
        _pipeline.SetMinimumSeverity(LogSeverity.Trace);
        _minimumSeverityChanged = true;
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

        if (_logCount < MaxLogCount)
        {
            Log.Trace(_logCount++, AppendTrace);
        }

        if (_logCount < MaxLogCount)
        {
            Log.Debug(_logCount++, AppendDebug);
        }

        if (_logCount < MaxLogCount)
        {
            Log.Info(_logCount++, AppendInfo);
        }

        if (_logCount < MaxLogCount)
        {
            Log.Warning(_logCount++, AppendWarning);
        }

        if (_logCount < MaxLogCount)
        {
            Log.Error(_logCount++, AppendError);
        }

        if (_logCount < MaxLogCount)
        {
            Log.Fatal(_logCount++, AppendFatal);
        }
    }

    private void OnDestroy()
    {
        if (_minimumSeverityChanged && _pipeline != null)
        {
            _pipeline.SetMinimumSeverity(_previousMinimumSeverity);
            _minimumSeverityChanged = false;
        }

        if (_fileSink != null && _pipeline != null && _pipeline.RemoveSink(_fileSink, 2000))
        {
            _fileSink.Dispose();
        }

        _fileSink = null;
        _pipeline = null;
    }

    private static void AppendTrace(int value, System.Text.StringBuilder builder) => builder.Append("Trace message ").Append(value);
    private static void AppendDebug(int value, System.Text.StringBuilder builder) => builder.Append("Debug message ").Append(value);
    private static void AppendInfo(int value, System.Text.StringBuilder builder) => builder.Append("Info message ").Append(value);
    private static void AppendWarning(int value, System.Text.StringBuilder builder) => builder.Append("Warning message ").Append(value);
    private static void AppendError(int value, System.Text.StringBuilder builder) => builder.Append("Error message ").Append(value);
    private static void AppendFatal(int value, System.Text.StringBuilder builder) => builder.Append("Fatal message ").Append(value);
}
