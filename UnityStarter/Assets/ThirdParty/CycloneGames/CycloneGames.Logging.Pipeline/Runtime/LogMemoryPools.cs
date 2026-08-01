using System;
using CycloneGames.Logging.Internal;

namespace CycloneGames.Logging
{
    /// <summary>
    /// Explicit owner for process-wide idle logging pools. These methods never drain a pipeline
    /// or touch an event that is queued, dispatched, or otherwise in flight.
    /// </summary>
    public static class LogMemoryPools
    {
        public static LogMemoryPoolStatistics GetStatistics()
        {
            LogEventPool.PoolStatistics events = LogEventPool.GetStatistics();
            StringBuilderPool.PoolStatistics builders = StringBuilderPool.GetStatistics();
            return new LogMemoryPoolStatistics(
                events.CurrentSize,
                events.PeakSize,
                events.TotalMisses,
                events.TotalDiscards,
                events.InvalidReturns,
                builders.CurrentSize,
                builders.PeakSize,
                builders.TotalMisses,
                builders.TotalDiscards);
        }

        /// <summary>
        /// Releases at most <paramref name="maxWork"/> idle entries from the process-wide pools.
        /// </summary>
        public static LogMemoryTrimResult TrimStep(
            int targetRetainedLogEvents,
            int targetRetainedStringBuilders,
            int maxWork)
        {
            if (targetRetainedLogEvents < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(targetRetainedLogEvents));
            }

            if (targetRetainedStringBuilders < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(targetRetainedStringBuilders));
            }

            if (maxWork < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxWork));
            }

            int releasedEvents = LogEventPool.TrimStep(targetRetainedLogEvents, maxWork);
            int remainingWork = maxWork - releasedEvents;
            int releasedBuilders = StringBuilderPool.TrimStep(targetRetainedStringBuilders, remainingWork);
            LogMemoryPoolStatistics statistics = GetStatistics();
            bool hasMore = statistics.RetainedLogEvents > targetRetainedLogEvents
                || statistics.RetainedStringBuilders > targetRetainedStringBuilders;
            return new LogMemoryTrimResult(
                releasedEvents + releasedBuilders,
                releasedEvents,
                releasedBuilders,
                statistics.RetainedLogEvents,
                statistics.RetainedStringBuilders,
                hasMore);
        }

        internal static void ClearIdleEntries()
        {
            LogEventPool.Clear();
            StringBuilderPool.Clear();
        }
    }
}

