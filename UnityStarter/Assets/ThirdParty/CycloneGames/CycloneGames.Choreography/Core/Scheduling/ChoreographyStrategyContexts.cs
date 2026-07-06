namespace CycloneGames.Choreography.Core
{
    /// <summary>
    /// Read-only context handed to <see cref="IPlaybackStrategy.Resolve"/> when a new request competes for a channel.
    /// Contains only the aggregate facts a strategy needs so strategies stay stateless and allocation-free.
    /// </summary>
    public readonly struct ChoreographyStrategyContext
    {
        public readonly int Channel;
        public readonly int IncomingPriority;

        /// <summary>Number of instances already active on the channel.</summary>
        public readonly int ActiveCountOnChannel;

        /// <summary>Highest priority among the instances already active on the channel.</summary>
        public readonly int HighestActivePriority;

        /// <summary>Whether the currently dominant instance's section allows interruption right now.</summary>
        public readonly bool DominantInterruptible;

        public ChoreographyStrategyContext(
            int channel,
            int incomingPriority,
            int activeCountOnChannel,
            int highestActivePriority,
            bool dominantInterruptible)
        {
            Channel = channel;
            IncomingPriority = incomingPriority;
            ActiveCountOnChannel = activeCountOnChannel;
            HighestActivePriority = highestActivePriority;
            DominantInterruptible = dominantInterruptible;
        }
    }

    /// <summary>
    /// Read-only context handed to <see cref="IPlaybackStrategy.ResolveWeight"/> each tick for one active sample.
    /// </summary>
    public readonly struct ChoreographyWeightContext
    {
        public readonly int Channel;
        public readonly int InstancePriority;
        public readonly int HighestPriorityOnChannel;
        public readonly int ActiveCountOnChannel;

        /// <summary>Authored clip weight for the sample being resolved.</summary>
        public readonly float AuthoredWeight;

        /// <summary>Sum of authored weights across all active samples on the channel (for normalization).</summary>
        public readonly float TotalAuthoredWeight;

        public ChoreographyWeightContext(
            int channel,
            int instancePriority,
            int highestPriorityOnChannel,
            int activeCountOnChannel,
            float authoredWeight,
            float totalAuthoredWeight)
        {
            Channel = channel;
            InstancePriority = instancePriority;
            HighestPriorityOnChannel = highestPriorityOnChannel;
            ActiveCountOnChannel = activeCountOnChannel;
            AuthoredWeight = authoredWeight;
            TotalAuthoredWeight = totalAuthoredWeight;
        }
    }
}
