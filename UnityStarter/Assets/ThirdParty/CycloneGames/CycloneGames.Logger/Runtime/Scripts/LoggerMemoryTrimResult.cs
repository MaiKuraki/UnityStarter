namespace CycloneGames.Logger
{
    /// <summary>
    /// Result of one bounded release pass over idle logger-owned pools.
    /// Queued and in-flight log messages are never part of this operation.
    /// </summary>
    public readonly struct LoggerMemoryTrimResult
    {
        internal LoggerMemoryTrimResult(
            int workConsumed,
            int releasedLogMessages,
            int releasedStringBuilders,
            int remainingLogMessages,
            int remainingStringBuilders,
            bool hasMoreIdleEntries)
        {
            WorkConsumed = workConsumed;
            ReleasedLogMessages = releasedLogMessages;
            ReleasedStringBuilders = releasedStringBuilders;
            RemainingLogMessages = remainingLogMessages;
            RemainingStringBuilders = remainingStringBuilders;
            HasMoreIdleEntries = hasMoreIdleEntries;
        }

        public int WorkConsumed { get; }

        public int ReleasedLogMessages { get; }

        public int ReleasedStringBuilders { get; }

        public int RemainingLogMessages { get; }

        public int RemainingStringBuilders { get; }

        public bool HasMoreIdleEntries { get; }
    }
}
