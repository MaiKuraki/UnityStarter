using CycloneGames.Logging;
using CycloneGames.Logging.Pipeline;
using UnityEngine;

namespace CycloneGames.Logging.Unity.Samples
{
    /// <summary>
    /// Displays bounded queue and cache observations. This sample is diagnostic only and is
    /// not a performance or zero-allocation proof.
    /// </summary>
    public sealed class LoggingPoolMonitor : MonoBehaviour
    {
        private static readonly LogChannel Log = LoggingSamplesLog.PoolMonitorChannel;

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

        [ContextMenu("Show Logging Statistics")]
        private void ShowStatistics()
        {
            if (!(LogRuntime.Writer is LogPipeline pipeline))
            {
                Log.Warning("Pipeline statistics are unavailable because LogPipeline is not the process writer.");
                return;
            }

            LogMemoryPoolStatistics memory = LogMemoryPools.GetStatistics();
            LogPipelineStatistics processing = pipeline.GetStatistics();
            Log.Info(
                $"Logging queue: {processing.QueuedCount} messages, {processing.QueuedCharacters} characters, "
                + $"peak {processing.PeakQueuedCount}/{processing.PeakQueuedCharacters}, dropped {processing.DroppedMessageCount}.\n"
                + $"Caches: messages {memory.RetainedLogEvents} (peak {memory.PeakRetainedLogEvents}, misses {memory.LogEventPoolMisses}), "
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
}
