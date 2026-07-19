namespace CycloneGames.Logger
{
    public readonly struct LoggerMemoryStatistics
    {
        public readonly int RetainedLogMessages;
        public readonly int PeakRetainedLogMessages;
        public readonly long LogMessagePoolMisses;
        public readonly long LogMessagePoolDiscards;
        public readonly long InvalidLogMessageReturns;
        public readonly int RetainedStringBuilders;
        public readonly int PeakRetainedStringBuilders;
        public readonly long StringBuilderPoolMisses;
        public readonly long StringBuilderPoolDiscards;

        internal LoggerMemoryStatistics(
            int retainedLogMessages,
            int peakRetainedLogMessages,
            long logMessagePoolMisses,
            long logMessagePoolDiscards,
            long invalidLogMessageReturns,
            int retainedStringBuilders,
            int peakRetainedStringBuilders,
            long stringBuilderPoolMisses,
            long stringBuilderPoolDiscards)
        {
            RetainedLogMessages = retainedLogMessages;
            PeakRetainedLogMessages = peakRetainedLogMessages;
            LogMessagePoolMisses = logMessagePoolMisses;
            LogMessagePoolDiscards = logMessagePoolDiscards;
            InvalidLogMessageReturns = invalidLogMessageReturns;
            RetainedStringBuilders = retainedStringBuilders;
            PeakRetainedStringBuilders = peakRetainedStringBuilders;
            StringBuilderPoolMisses = stringBuilderPoolMisses;
            StringBuilderPoolDiscards = stringBuilderPoolDiscards;
        }
    }
}
