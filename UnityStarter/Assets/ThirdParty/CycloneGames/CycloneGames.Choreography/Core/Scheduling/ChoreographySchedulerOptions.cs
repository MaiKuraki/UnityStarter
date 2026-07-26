using System;

namespace CycloneGames.Choreography.Core
{
    /// <summary>Explicit retained-container limits for one scheduler instance.</summary>
    public readonly struct ChoreographySchedulerOptions
    {
        public const int DefaultMaximumActiveCount = 4_096;
        public const int DefaultMaximumQueuedCount = 16_384;
        public const int DefaultMaximumRetainedPoolCount = 256;
        public const int AbsoluteMaximumActiveCount = 65_536;
        public const int AbsoluteMaximumQueuedCount = 262_144;
        public const int AbsoluteMaximumRetainedPoolCount = 4_096;

        public ChoreographySchedulerOptions(
            int maximumActiveCount = DefaultMaximumActiveCount,
            int maximumQueuedCount = DefaultMaximumQueuedCount,
            int maximumRetainedPoolCount = DefaultMaximumRetainedPoolCount)
        {
            if (maximumActiveCount <= 0 || maximumActiveCount > AbsoluteMaximumActiveCount)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumActiveCount));
            }
            if (maximumQueuedCount <= 0 || maximumQueuedCount > AbsoluteMaximumQueuedCount)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumQueuedCount));
            }
            if (maximumRetainedPoolCount < 0 || maximumRetainedPoolCount > AbsoluteMaximumRetainedPoolCount)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumRetainedPoolCount));
            }

            MaximumActiveCount = maximumActiveCount;
            MaximumQueuedCount = maximumQueuedCount;
            MaximumRetainedPoolCount = maximumRetainedPoolCount;
        }

        public int MaximumActiveCount { get; }
        public int MaximumQueuedCount { get; }
        public int MaximumRetainedPoolCount { get; }

        public static ChoreographySchedulerOptions Default => new ChoreographySchedulerOptions(
            DefaultMaximumActiveCount,
            DefaultMaximumQueuedCount,
            DefaultMaximumRetainedPoolCount);
    }
}
