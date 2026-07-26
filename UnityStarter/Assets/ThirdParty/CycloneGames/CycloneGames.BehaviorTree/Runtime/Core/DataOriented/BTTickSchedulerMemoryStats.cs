namespace CycloneGames.BehaviorTree.Runtime.DOD
{
    public readonly struct BTTickSchedulerMemoryStats
    {
        public BTTickSchedulerMemoryStats(
            int agentSlotCount,
            int activeAgentCount,
            int agentCapacity,
            int maximumAgentCount,
            int peakActiveAgentCount,
            long capacityRejectedAgentCount,
            long retainedNativeElementBytes)
        {
            AgentSlotCount = agentSlotCount;
            ActiveAgentCount = activeAgentCount;
            AgentCapacity = agentCapacity;
            MaximumAgentCount = maximumAgentCount;
            PeakActiveAgentCount = peakActiveAgentCount;
            CapacityRejectedAgentCount = capacityRejectedAgentCount;
            RetainedNativeElementBytes = retainedNativeElementBytes;
        }

        public int AgentSlotCount { get; }
        public int ActiveAgentCount { get; }
        public int AgentCapacity { get; }
        public int MaximumAgentCount { get; }
        public int PeakActiveAgentCount { get; }
        public long CapacityRejectedAgentCount { get; }
        public long RetainedNativeElementBytes { get; }
    }
}
