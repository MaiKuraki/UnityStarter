namespace CycloneGames.Logging
{
    public readonly struct LogMemoryPoolStatistics
    {
        public readonly int RetainedLogEvents;
        public readonly int PeakRetainedLogEvents;
        public readonly long LogEventPoolMisses;
        public readonly long LogEventPoolDiscards;
        public readonly long InvalidLogEventReturns;
        public readonly int RetainedStringBuilders;
        public readonly int PeakRetainedStringBuilders;
        public readonly long StringBuilderPoolMisses;
        public readonly long StringBuilderPoolDiscards;

        internal LogMemoryPoolStatistics(
            int retainedLogEvents,
            int peakRetainedLogEvents,
            long logEventPoolMisses,
            long logEventPoolDiscards,
            long invalidLogEventReturns,
            int retainedStringBuilders,
            int peakRetainedStringBuilders,
            long stringBuilderPoolMisses,
            long stringBuilderPoolDiscards)
        {
            RetainedLogEvents = retainedLogEvents;
            PeakRetainedLogEvents = peakRetainedLogEvents;
            LogEventPoolMisses = logEventPoolMisses;
            LogEventPoolDiscards = logEventPoolDiscards;
            InvalidLogEventReturns = invalidLogEventReturns;
            RetainedStringBuilders = retainedStringBuilders;
            PeakRetainedStringBuilders = peakRetainedStringBuilders;
            StringBuilderPoolMisses = stringBuilderPoolMisses;
            StringBuilderPoolDiscards = stringBuilderPoolDiscards;
        }
    }
}
