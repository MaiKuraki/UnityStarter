using CycloneGames.Logger;
using CycloneGames.Logging;
using UnityEngine;

/// <summary>
/// Displays bounded queue and cache observations. This sample is diagnostic only and is
/// not a performance or zero-allocation proof.
/// </summary>
public sealed class LoggerPoolMonitor : MonoBehaviour
{
    private static readonly LogChannel Log = LoggerSamplesLog.PoolMonitorChannel;

    [SerializeField] private int BurstLogCount = 5000;
    [SerializeField] private float MonitorIntervalSeconds = 1.0f;

    private float _lastMonitorTime;
    private bool _burstCompleted;

    private void Update()
    {
        if (!_burstCompleted && Time.time > 2.0f)
        {
            RunBurstExample();
            _burstCompleted = true;
        }

        if (Time.time - _lastMonitorTime >= MonitorIntervalSeconds)
        {
            ShowStatistics();
            _lastMonitorTime = Time.time;
        }
    }

    [ContextMenu("Show Logger Statistics")]
    private void ShowStatistics()
    {
        if (!(LogRuntime.Writer is CLogger backend))
        {
            Log.Warning("Logger processing statistics are unavailable because CLogger is not the process writer.");
            return;
        }

        LoggerMemoryStatistics memory = CLogger.GetMemoryStatistics();
        LogProcessingStatistics processing = backend.GetProcessingStatistics();
        Log.Info(
            $"Logger queue: {processing.QueuedCount} messages, {processing.QueuedCharacters} characters, "
            + $"peak {processing.PeakQueuedCount}/{processing.PeakQueuedCharacters}, dropped {processing.DroppedMessageCount}.\n"
            + $"Caches: messages {memory.RetainedLogMessages} (peak {memory.PeakRetainedLogMessages}, misses {memory.LogMessagePoolMisses}), "
            + $"builders {memory.RetainedStringBuilders} (peak {memory.PeakRetainedStringBuilders}, misses {memory.StringBuilderPoolMisses}).");
    }

    [ContextMenu("Run Bounded Burst Example")]
    private void RunBurstExample()
    {
        for (int i = 0; i < BurstLogCount; i++)
        {
            Log.Info(i, static (value, builder) => builder.Append("Burst message ").Append(value));
        }

        ShowStatistics();
    }
}
