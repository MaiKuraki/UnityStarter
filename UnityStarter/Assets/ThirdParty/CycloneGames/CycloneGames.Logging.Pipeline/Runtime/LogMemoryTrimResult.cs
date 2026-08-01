namespace CycloneGames.Logging
{
    /// <summary>
    /// Result of one bounded release pass over idle logging pools.
    /// Queued and in-flight log messages are never part of this operation.
    /// </summary>
    public readonly struct LogMemoryTrimResult
    {
        internal LogMemoryTrimResult(
            int workConsumed,
            int releasedLogEvents,
            int releasedStringBuilders,
            int remainingLogEvents,
            int remainingStringBuilders,
            bool hasMoreIdleEntries)
        {
            WorkConsumed = workConsumed;
            ReleasedLogEvents = releasedLogEvents;
            ReleasedStringBuilders = releasedStringBuilders;
            RemainingLogEvents = remainingLogEvents;
            RemainingStringBuilders = remainingStringBuilders;
            HasMoreIdleEntries = hasMoreIdleEntries;
        }

        public int WorkConsumed { get; }

        public int ReleasedLogEvents { get; }

        public int ReleasedStringBuilders { get; }

        public int RemainingLogEvents { get; }

        public int RemainingStringBuilders { get; }

        public bool HasMoreIdleEntries { get; }
    }
}
