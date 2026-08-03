using System.IO;
using CycloneGames.Logging;
using CycloneGames.Logging.Pipeline;
using UnityEngine;

namespace CycloneGames.Logging.Unity.Samples
{
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
        private bool _completionReported;

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
            LogSinkRegistrationResult registration = _pipeline.RegisterSink(
                _fileSink,
                LogSinkRegistrationMode.UniqueExactType);
            if (!registration.IsRegistered)
            {
                if (registration.CallerRetainsOwnership)
                {
                    _fileSink.Dispose();
                }

                _fileSink = null;
            }
#endif
            _previousMinimumSeverity = _pipeline.MinimumSeverity;
            _pipeline.MinimumSeverity = LogSeverity.Trace;
            _minimumSeverityChanged = true;
            _startTime = Time.time;
        }

        private void Update()
        {
            if (_logCount >= MaxLogCount)
            {
                if (!_completionReported)
                {
                    Log.Info($"Submitted {MaxLogCount} sample messages in {Time.time - _startTime:F2} seconds.");
                    _completionReported = true;
                }

                if (TryCleanup())
                {
                    enabled = false;
                }

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

        private void OnDisable()
        {
            TryCleanup();
        }

        private void OnDestroy()
        {
            TryCleanup();
        }

        private bool TryCleanup()
        {
            if (_minimumSeverityChanged && _pipeline != null)
            {
                _pipeline.MinimumSeverity = _previousMinimumSeverity;
                _minimumSeverityChanged = false;
            }

            if (_fileSink != null)
            {
                if (_pipeline == null || !_pipeline.RemoveSink(_fileSink, 2000))
                {
                    return false;
                }

                FileLogSink sink = _fileSink;
                _fileSink = null;
                sink.Dispose();
            }

            _pipeline = null;
            return true;
        }

        private static void AppendTrace(int value, System.Text.StringBuilder builder) => builder.Append("Trace message ").Append(value);
        private static void AppendDebug(int value, System.Text.StringBuilder builder) => builder.Append("Debug message ").Append(value);
        private static void AppendInfo(int value, System.Text.StringBuilder builder) => builder.Append("Info message ").Append(value);
        private static void AppendWarning(int value, System.Text.StringBuilder builder) => builder.Append("Warning message ").Append(value);
        private static void AppendError(int value, System.Text.StringBuilder builder) => builder.Append("Error message ").Append(value);
        private static void AppendFatal(int value, System.Text.StringBuilder builder) => builder.Append("Fatal message ").Append(value);
    }
}
